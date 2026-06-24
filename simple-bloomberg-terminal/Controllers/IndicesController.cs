using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

[Route("indices")]
[Authorize(Roles = "Admin,Manager")]
public class IndicesController : Controller
{
    private readonly IStockIndexRepository _indices;
    private readonly IIndexDiscovery _discovery;
    private readonly IIndexImportJobRepository _jobs;     // durable, globally-visible job rows
    private readonly IndexImportJobStore _progress;        // in-memory live phase text for running jobs
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserApiKeyProvider _keys;

    public IndicesController(IStockIndexRepository indices, IIndexDiscovery discovery,
        IIndexImportJobRepository jobs, IndexImportJobStore progress,
        IServiceScopeFactory scopeFactory, IUserApiKeyProvider keys)
    {
        _indices = indices;
        _discovery = discovery;
        _jobs = jobs;
        _progress = progress;
        _scopeFactory = scopeFactory;
        _keys = keys;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() =>
        View(new IndicesPageViewModel(_indices.GetAll().ToList(), _jobs.Recent()));

    [AllowAnonymous]
    [HttpGet, Route("{id:long}/breakdown")]
    public IActionResult Details(long id)
    {
        var index = _indices.GetWithConstituents(id);
        if (index == null) return NotFound();

        // Group by the LIVE Company.Sector / Industry, weighted by the stored per-member WeightPct.
        // Members whose Company failed to load (shouldn't happen with Restrict) are ignored.
        var members = index.Constituents.Where(c => c.Company != null).ToList();

        var bySector = members
            .GroupBy(c => c.Company!.Sector.ToString())
            .Select(g => new IndexBreakdownSlice(Humanize(g.Key), g.Count(), RoundWeight(g)))
            .OrderByDescending(s => s.WeightPct).ThenByDescending(s => s.CompanyCount)
            .ToList();

        var byIndustry = members
            .GroupBy(c => c.Company!.Industry?.ToString() ?? "UNCLASSIFIED")
            .Select(g => new IndexBreakdownSlice(Humanize(g.Key), g.Count(), RoundWeight(g)))
            .OrderByDescending(s => s.WeightPct).ThenByDescending(s => s.CompanyCount)
            .ToList();

        return View(new IndexDetailViewModel
        {
            Index = index,
            BySector = bySector,
            ByIndustry = byIndustry,
            MemberCount = members.Count,
            WeightCovered = Math.Round(members.Sum(c => c.WeightPct ?? 0), 2)
        });
    }

    // Web-search the indices matching a free-text query (Perplexity). Returns the suggestions as JSON
    // for the page to render as one-click import buttons; persists nothing.
    [HttpPost, Route("discover"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Discover(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query)) return BadRequest("Enter a query.");
        var results = await _discovery.DiscoverAsync(query.Trim(), ct);
        return Json(results);
    }

    // Start an import as a detached background job and hand back its id; the page polls Status until it
    // finishes (slow Wikipedia scrape / SPDR fetch + SEC map must not time out the request). Works for
    // a discovered suggestion (code+wikiPage+sector+etfTicker) and the manual SPDR box (etfTicker only).
    [HttpPost, Route("import"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Import(string? code, string? name, string? wikiPage, string? etfTicker,
        string? sector, string? region)
    {
        if (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(etfTicker))
            return BadRequest("Provide an index or a SPDR ETF ticker.");

        Sector? parsedSector = Enum.TryParse<Sector>(sector, ignoreCase: true, out var s) ? s : null;
        var request = new IndexImportRequest(
            Code: code?.Trim() ?? "",
            Name: name?.Trim() ?? "",
            WikiPage: string.IsNullOrWhiteSpace(wikiPage) ? null : wikiPage.Trim(),
            EtfTicker: string.IsNullOrWhiteSpace(etfTicker) ? null : etfTicker.Trim(),
            Sector: parsedSector,
            Region: string.IsNullOrWhiteSpace(region) ? null : region.Trim());

        // Snapshot the user's keys now (the detached scope has no HttpContext): the import auto-provisions
        // missing members from FMP, which is a bring-your-own key. No key => provisioning self-disables
        // and the import links only what already exists (and the job lands Partial for someone to continue).
        var keys = await _keys.GetAsync();

        // Persist the job (with the request, so a continue can replay it) before kicking off the work.
        var job = new IndexImportJob
        {
            Label = FirstNonBlank(name, code, etfTicker),
            Code = request.Code,
            Name = request.Name,
            WikiPage = request.WikiPage,
            EtfTicker = request.EtfTicker,
            Sector = request.Sector,
            Region = request.Region,
            StartedBy = User.Identity?.Name
        };
        _jobs.Add(job);

        RunDetached(job.Id, request, keys);
        return Json(new { jobId = job.Id });
    }

    // Continue a job whose FMP auto-provisioning was cut short (Partial) — or retry one that errored —
    // under THIS user's keys. Re-runs the stored request: existing members re-link for free via the SEC
    // CIK map, and only the still-missing members spend the continuing user's FMP quota. Any authorized
    // user may continue any user's job — that's the point (one person's spent key shouldn't strand it).
    [HttpPost, Route("import/{id:long}/continue", Name = "StockIndexImportContinue"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Continue(long id)
    {
        var job = _jobs.Get(id);
        if (job is null) return NotFound();
        if (job.Status == ImportJobStatus.Running) return BadRequest("That import is already running.");

        var request = new IndexImportRequest(job.Code, job.Name, job.WikiPage, job.EtfTicker, job.Sector, job.Region);
        var keys = await _keys.GetAsync();

        // Flip to Running and stamp who's continuing before the detached work starts (it has no HttpContext).
        job.Status = ImportJobStatus.Running;
        job.ContinuedBy = User.Identity?.Name;
        job.Error = null;
        _jobs.Update(job);

        RunDetached(job.Id, request, keys);
        return Json(new { jobId = job.Id });
    }

    // The shared detached runner for both a fresh import and a continue. Opens its own DI scope (the
    // request's is gone once the action returns), runs the idempotent import, and writes the durable
    // outcome — Done when provisioning ran to completion, Partial when it stopped on a missing key / FMP
    // cap (so the jobs list offers a Continue). Live phase text goes to the in-memory overlay, not the DB.
    private void RunDetached(long jobId, IndexImportRequest request, UserApiKeys keys)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var import = sp.GetRequiredService<IIndexImportService>();
            var jobs = sp.GetRequiredService<IIndexImportJobRepository>();
            var progress = new Progress<string>(msg => _progress.SetProgress(jobId, msg));

            var job = jobs.Get(jobId);
            if (job is null) return;
            try
            {
                // Phase 1: profile-only provisioning + connect. Fast, so the breakdown opens promptly.
                var result = await import.ImportAsync(request, progress);
                job.IndexId = result.IndexId;
                job.TotalConstituents = result.TotalConstituents;
                job.Matched = result.Matched;
                job.Provisioned = result.Provisioned;
                var added = result.Provisioned > 0 ? $"{result.Provisioned} newly added, " : "";
                job.Message =
                    $"{result.IndexName}: matched {result.Matched} of {result.TotalConstituents} members " +
                    $"({added}{result.WeightCovered:0.##}% weight, via {result.Source}).";
                job.Status = result.CanContinue ? ImportJobStatus.Partial : ImportJobStatus.Done;
                jobs.Update(job);

                // Phase 2: fill financials + industry for the just-added members in the background. The
                // page has already navigated to the breakdown (weights are correct from MarketCap); this
                // just completes each new company's data. Best-effort — never flips the job to Error.
                if (result.ProvisionedIds.Count > 0)
                {
                    var provisioning = sp.GetRequiredService<ICompanyProvisioningService>();
                    try { await provisioning.EnrichAsync(result.ProvisionedIds, progress); }
                    catch { /* enrichment is best-effort; the index is already imported */ }
                }
            }
            catch (Exception ex)
            {
                job.Status = ImportJobStatus.Error;
                job.Error = FriendlyError(ex);
            }
            finally
            {
                job.CompletedAt = DateTime.UtcNow;
                jobs.Update(job);
                _progress.Clear(jobId);
            }
        });
    }

    // Poll one import job's live status. The page uses it to show progress and, on success, link to the
    // breakdown of the index that was just imported.
    [HttpGet, Route("import/{id:long}/status")]
    public IActionResult ImportStatus(long id)
    {
        var job = _jobs.Get(id);
        if (job is null) return NotFound();
        return Json(new
        {
            status = job.Status.ToString(),
            progress = _progress.GetProgress(id) ?? "",
            indexId = job.IndexId,
            message = job.Message,
            error = job.Error
        });
    }

    [HttpPost, Route("{id:long}/delete", Name = "StockIndexDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        _indices.SoftDelete(id);
        return RedirectToAction(nameof(Index));
    }

    private static string FriendlyError(Exception ex) => ex switch
    {
        ArgumentException => "Invalid index (bad Wikipedia page / ticker).",
        InvalidOperationException => ex.Message,    // e.g. "no constituents" — already user-facing
        HttpRequestException h => $"Source request failed ({(int?)h.StatusCode}).",
        _ => "Import failed."
    };

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "index";

    private static double RoundWeight(IEnumerable<IndexConstituent> g) =>
        Math.Round(g.Sum(c => c.WeightPct ?? 0), 2);

    // "INFORMATION_TECHNOLOGY" -> "Information Technology" for display.
    private static string Humanize(string enumName)
    {
        var words = enumName.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => w.Length == 0 ? w : char.ToUpperInvariant(w[0]) + w[1..].ToLowerInvariant());
        return string.Join(' ', words);
    }
}
