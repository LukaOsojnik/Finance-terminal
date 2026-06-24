using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

[Route("companies")]
// Any authenticated user — keyed actions (FMP fetch, private discovery, rediscover) run on the
// USER's own API keys. Browse actions stay [AllowAnonymous] (overrides below). A missing key shows
// the "add your key" popup; logged-out callers get the sign-in prompt.
[Authorize]
public class CompaniesController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly ICompanyFinancialsService _financials;
    private readonly ICompanyProfileDiscovery _profileDiscovery;
    private readonly ICompanyProvisioningService _provisioning;
    private readonly RediscoverJobStore _rediscoverJobs;
    private readonly BackfillJobStore _backfillJobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserApiKeyProvider _keys;

    public CompaniesController(ICompanyRepository companies,
        ICompanyFinancialsService financials,
        ICompanyProfileDiscovery profileDiscovery, ICompanyProvisioningService provisioning,
        RediscoverJobStore rediscoverJobs, BackfillJobStore backfillJobs,
        IServiceScopeFactory scopeFactory, IUserApiKeyProvider keys)
    {
        _rediscoverJobs = rediscoverJobs;
        _backfillJobs = backfillJobs;
        _scopeFactory = scopeFactory;
        _keys = keys;
        _companies = companies;
        _financials = financials;
        _profileDiscovery = profileDiscovery;
        _provisioning = provisioning;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() => View(_companies.GetAll());

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) =>
        PartialView("_TableBody", _companies.Search(term));

    [AllowAnonymous]
    [HttpGet, Route("lookup")]
    public IActionResult Lookup(string? term) =>
        Json(_companies.Lookup(term).Take(10).Select(c => new { id = c.Id, label = c.Name }));

    [AllowAnonymous]
    [HttpGet, Route("{id:long}/profile")]
    public IActionResult Details(long id)
    {
        // GetWithGraphRelations loads sources (with RelatedCompany + Filing) and events; Includes
        // don't filter soft-deleted rows, so filter to active here per the soft-delete convention.
        var company = _companies.GetWithGraphRelations(id);
        if (company == null) return NotFound();

        var vm = new CompanyDetailsViewModel
        {
            Company = company,
            RelatedEvents = company.Events.Where(e => e.DeletedAt == null),
            // Status filter hides Pending user contributions from the public profile until a Manager
            // approves them; Approved is the default so all existing/admin data still shows.
            RevenueSources = company.RevenueSources.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved),
            CostSources = company.CostSources.Where(c => c.DeletedAt == null && c.Status == ContributionStatus.Approved),
            CompanyRisks = company.CompanyRisks.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved),
            Financials = company.Financials.Where(f => f.DeletedAt == null)
                .OrderByDescending(f => f.EndDate ?? DateOnly.FromDateTime(DateTime.MinValue))
                .ThenByDescending(f => f.FiscalYear),
            SectorLabel = company.Sector.ToString().Replace("_", " "),
            IndustryLabel = company.Industry.HasValue
                ? company.Industry.Value.ToString().Replace("_", " ")
                : "—",
            SubIndustryLabel = company.GicsSubIndustry.HasValue
                ? company.GicsSubIndustry.Value.ToString().Replace("_SUB", "").Replace("_", " ")
                : "—"
        };
        return View(vm);
    }

    // Named route: both this MVC controller and the API CompaniesController have a "Create"
    // action, so a bare asp-action="Create" link is ambiguous and resolves to /api/Companies.
    // The name lets the view target this GET form unambiguously.
    [HttpGet, Route("create", Name = "CompaniesCreate")]
    public IActionResult Create()
    {
        PopulateDropdowns();
        return View(new CompanyCreateModel());
    }

    // Prefill the create form from FMP by ticker. Does not save — returns the Create view with
    // the mapped model so the user reviews/edits before submitting the normal Create POST.
    [HttpPost, Route("fetch"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Fetch(string? symbol)
    {
        PopulateDropdowns();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            ModelState.AddModelError("", "Enter a ticker symbol.");
            return View("Create", new CompanyCreateModel());
        }

        var ticker = symbol.Trim();
        CompanyDraft? draft;
        try { draft = await _provisioning.BuildFromTickerAsync(ticker); }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "FMP is unreachable. Try again or enter the company manually.");
            return View("Create", new CompanyCreateModel());
        }
        // No FMP key on this account: this is a full-page form, so surface guidance inline rather
        // than the AJAX popup the keyed buttons get.
        catch (MissingApiKeyException ex)
        {
            ModelState.AddModelError("", $"{ex.Message}. Add your FMP API key under Profile ▸ API Keys, then try again.");
            return View("Create", new CompanyCreateModel());
        }

        if (draft is null)
        {
            ModelState.AddModelError("", $"No company found for ticker '{ticker}'.");
            return View("Create", new CompanyCreateModel());
        }

        if (draft.CountryLabel != null) ViewBag.CountryLabel = draft.CountryLabel;
        if (draft.Note != null) ViewBag.FetchNote = draft.Note;
        // Flags the banner + per-field "auto-filled" glow on the Create view.
        ViewBag.Fetched = true;
        return View("Create", draft.Model);
    }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = CompanyMapper.ToEntity(model, model.CountryId);
        _companies.Add(entity);

        // Prefilled from a ticker -> pull the dated financial history (FMP, or Yahoo fallback) and
        // store it against the new company. Best-effort: an API failure must not block the create.
        if (!string.IsNullOrWhiteSpace(model.Symbol))
        {
            try { _companies.ReplaceFinancials(entity.Id, await _financials.BuildAsync(entity.Id, model.Symbol)); }
            catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials unavailable — company still created */ }
        }
        // Private company with an AI-estimated revenue: store it as a single CLAUDE_ESTIMATED history
        // row so the Details matrix shows it clearly flagged as an estimate (not reported API data).
        else if (model.Type == CompanyType.PRIVATE && entity.RevenueTotal is { } rev)
        {
            var fy = entity.AsOf?.Year ?? DateTime.UtcNow.Year;
            _companies.ReplaceFinancials(entity.Id, [new CompanyFinancial(entity.Id, fy, FiscalPeriod.FY)
            {
                EndDate = entity.AsOf,
                Source = DataSource.CLAUDE_ESTIMATED,
                CapturedAt = DateTime.UtcNow,
                Revenue = rev,
                GrossMargin = entity.GrossMargin
            }]);
        }
        return RedirectToAction(nameof(Index));
    }

    // Prefill the create form for a PRIVATE company (no ticker) from a web-search profile. Like Fetch,
    // it saves nothing — the user reviews the AI-estimated fields before submitting the Create POST.
    [HttpPost, Route("discover-private"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscoverPrivate(string? name)
    {
        PopulateDropdowns();
        ViewBag.PrivateMode = true;

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("", "Enter a company name.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE });
        }

        var companyName = name.Trim();
        CompanyProfileResult? result;
        try { result = await _profileDiscovery.DiscoverAsync(companyName); }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "AI discovery is unreachable. Try again or enter the company manually.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }
        // No Perplexity key on this account: full-page form, so guide inline.
        catch (MissingApiKeyException ex)
        {
            ModelState.AddModelError("", $"{ex.Message}. Add your Perplexity API key under Profile ▸ API Keys, then try again.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }

        if (result is null)
        {
            ModelState.AddModelError("", $"Couldn't find a profile for '{companyName}'. Enter it manually.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }

        var draft = await _provisioning.BuildPrivateAsync(result, companyName);
        if (draft.CountryLabel != null) ViewBag.CountryLabel = draft.CountryLabel;
        ViewBag.Fetched = true;
        ViewBag.FetchNote = "Profile and financials are AI-estimated from web search — review before saving.";
        return View("Create", draft.Model);
    }

    // Re-run Perplexity discovery for an EXISTING private company and overwrite its profile fields in
    // place (the Details "RE-DISCOVER" button). Detached: the ~90s sonar-pro call + LLM industry +
    // country lookup run on a background task with a fresh DI scope (the request-scoped DbContext is
    // gone once this returns), so a slow/unreliable web search can't time out the request. Returns a
    // job id the bottom-right widget polls. Public companies are rejected — they have a ticker.
    [HttpPost, Route("{id:long}/rediscover", Name = "CompanyRediscover"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Rediscover(long id)
    {
        var entity = _companies.GetById(id);
        if (entity == null) return NotFound();
        if (entity.Type != CompanyType.PRIVATE) return BadRequest("Re-discovery is for private companies only.");

        // Re-discovery runs Perplexity sonar. Verify the user's key now (throw -> 424 popup) and
        // snapshot the keys so the detached background scope can use them (it has no HttpContext).
        var keys = await _keys.GetAsync();
        if (string.IsNullOrWhiteSpace(keys.Perplexity)) throw MissingApiKeyException.Perplexity();

        var job = new RediscoverJob { CompanyId = id, CompanyName = entity.Name };
        _rediscoverJobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var companies = sp.GetRequiredService<ICompanyRepository>();
            var discovery = sp.GetRequiredService<ICompanyProfileDiscovery>();
            var provisioning = sp.GetRequiredService<ICompanyProvisioningService>();
            try
            {
                var company = companies.GetById(job.CompanyId);
                if (company is null) { job.Status = ScanJobStatus.Error; job.Error = "Company no longer exists."; return; }

                job.Progress = "Searching the web with sonar-pro…";
                var result = await discovery.DiscoverAsync(company.Name);
                if (result is null) { job.Status = ScanJobStatus.Error; job.Error = "No profile found from web search."; return; }

                job.Progress = "Mapping sector / industry / country…";
                var model = (await provisioning.BuildPrivateAsync(result, company.Name)).Model;

                // Park the proposal for the user to judge in the widget — DO NOT save yet. ACCEPT applies
                // this; REJECT dismisses it. Summarise proposed-vs-current so the verdict is informed.
                static string B(double? v) => v is { } x ? "$" + (x / 1e9).ToString("F2") + "B" : "—";
                static string P(double? v) => v is { } x ? (x * 100).ToString("0.#") + "%" : "—";
                var yr = model.AsOf?.Year;
                job.Proposed = model;
                job.Sources = result.Sources;
                job.Result =
                    $"Proposed: rev {B(model.RevenueTotal)}{(yr is { } y ? $" (FY{y})" : "")}, margin {P(model.GrossMargin)}, " +
                    $"valuation {B(model.MarketCap)}, " +
                    $"{model.Sector.ToString().Replace('_', ' ')}{(model.Industry is { } ind ? " / " + ind.ToString().Replace('_', ' ') : "")}. " +
                    $"Current: rev {B(company.RevenueTotal)}, margin {P(company.GrossMargin)}, valuation {B(company.MarketCap)}.";

                job.Progress = "";
                job.Status = ScanJobStatus.Done;   // ready for review (not saved)
            }
            catch (Exception ex)
            {
                job.Status = ScanJobStatus.Error;
                job.Error = ex.Message;
                job.Progress = "";
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        });

        return Json(new { jobId = job.Id });
    }

    // ACCEPT a finished re-discovery from the widget: apply its parked proposal to the company and save.
    // Idempotent. REJECT has no endpoint — the widget just dismisses the job (nothing was written).
    [HttpPost, Route("rediscover/{jobId}/accept", Name = "CompanyRediscoverAccept"), ValidateAntiForgeryToken]
    public IActionResult AcceptRediscover(string jobId)
    {
        var job = _rediscoverJobs.Get(jobId);
        if (job?.Proposed is null) return NotFound();
        if (job.Applied) return Ok();   // already accepted — no-op

        var company = _companies.GetById(job.CompanyId);
        if (company is null) return NotFound();

        var model = job.Proposed;
        _provisioning.ApplyFetchedData(company, model);
        // Mirror the background rule: a fresh revenue carries a fresh margin, so overwrite the old margin
        // too (even with null) rather than letting ApplyFetchedData keep a stale guess.
        if (model.RevenueTotal is not null) company.GrossMargin = model.GrossMargin;
        _companies.Update(company);

        // Re-stamp the single estimated FY row so the Details matrix matches the accepted revenue/margin.
        if (company.RevenueTotal is { } rev)
        {
            var fy = company.AsOf?.Year ?? DateTime.UtcNow.Year;
            _companies.ReplaceFinancials(company.Id, [new CompanyFinancial(company.Id, fy, FiscalPeriod.FY)
            {
                EndDate = company.AsOf,
                Source = DataSource.CLAUDE_ESTIMATED,
                CapturedAt = DateTime.UtcNow,
                Revenue = rev,
                GrossMargin = company.GrossMargin
            }]);
        }

        job.Applied = true;
        return Ok();
    }

    // Backfill #1 — real financial history + profile refresh from FMP/Yahoo. Runs DETACHED so the page
    // can show live per-company progress and Cancel can stop it. Returns the job id to poll.
    [HttpPost, Route("backfill/financials"), ValidateAntiForgeryToken]
    public async Task<IActionResult> BackfillFinancials()
    {
        var keys = await _keys.GetAsync();   // captured here (the detached task has no HttpContext)
        var job = new BackfillJob();
        _backfillJobs.Add(job);
        RunBackfillDetached(job.Id, keys, (svc, p, ct) => svc.BackfillFinancialsAsync(p, ct));
        return Json(new { jobId = job.Id });
    }

    // Backfill #2 — resolve missing GICS sub-industries (cheap LLM, cache-deduped). Same detached
    // progress/cancel machinery; Cancel aborts the in-flight LLM call.
    [HttpPost, Route("backfill/industries"), ValidateAntiForgeryToken]
    public async Task<IActionResult> BackfillIndustries()
    {
        var keys = await _keys.GetAsync();
        var job = new BackfillJob();
        _backfillJobs.Add(job);
        RunBackfillDetached(job.Id, keys, (svc, p, ct) => svc.BackfillIndustriesAsync(p, ct));
        return Json(new { jobId = job.Id });
    }

    // Poll one backfill job: returns the progress lines the browser hasn't seen yet (sliced from `since`),
    // the new cursor, and — once done — the final summary for the results popup.
    [HttpGet, Route("backfill/{id}/status")]
    public IActionResult BackfillStatus(string id, int since = 0)
    {
        var job = _backfillJobs.Get(id);
        if (job is null) return NotFound();

        string[] lines;
        int total;
        lock (job.Lock) { total = job.Lines.Count; lines = job.Lines.Skip(since).ToArray(); }

        var result = job.Done && job.Error is null && job.Result is { } r
            ? new
            {
                filled = r.Filled, failed = r.Failed, industriesFilled = r.IndustriesFilled,
                rateLimited = r.RateLimited, remaining = r.Remaining, message = r.Message
            }
            : null;
        return Json(new { lines, nextSince = total, done = job.Done, error = job.Error, result });
    }

    // Close button -> cancel the run: trips the job's token, which aborts the in-flight LLM request and
    // stops the loop (the partial result so far is still returned on the next status poll).
    [HttpPost, Route("backfill/{id}/cancel"), ValidateAntiForgeryToken]
    public IActionResult BackfillCancel(string id)
    {
        var job = _backfillJobs.Get(id);
        if (job is null) return NotFound();
        job.Cts.Cancel();
        return Ok();
    }

    // Detached runner shared by both backfills: own DI scope (the request's is gone once the action
    // returns), the user's captured keys set on it so the FMP/LLM clients are authenticated, progress
    // appended to the job for polling. `run` selects which backfill (financials or industries) to drive.
    private void RunBackfillDetached(string jobId, UserApiKeys keys,
        Func<ICompanyProvisioningService, IProgress<string>, CancellationToken, Task<BackfillResult>> run)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var provisioning = sp.GetRequiredService<ICompanyProvisioningService>();
            var jobs = sp.GetRequiredService<BackfillJobStore>();   // singleton — same instance the poll reads
            var job = jobs.Get(jobId);
            if (job is null) return;

            var progress = new SyncProgress<string>(line => { lock (job.Lock) job.Lines.Add(line); });
            try
            {
                job.Result = await run(provisioning, progress, job.Cts.Token);
            }
            catch (HttpRequestException) { job.Error = "SEC ticker map is unreachable. Try again."; }
            catch (OperationCanceledException) { /* cancelled before any partial result — just mark done */ }
            catch (Exception ex) { job.Error = ex.Message; }
            finally { job.Done = true; }
        });
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _companies.GetById(id);
        if (entity == null) return NotFound();
        PopulateDropdowns();
        ViewBag.CountryLabel = entity.Country?.Name;
        return View("Edit", ToEditModel(entity));
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _companies.GetById(id);
        if (entity == null) return NotFound();

        var model = ToEditModel(entity);
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { PopulateDropdowns(); ViewBag.CountryLabel = entity.Country?.Name; return View("Edit", model); }

        ApplyEdit(entity, model);
        _companies.Update(entity);
        return RedirectToAction(nameof(Index));
    }

    // Returns status codes (not a redirect) because the row-delete is driven by fetch in site.js;
    // a redirect would resolve to a 200 page and the JS would treat a blocked delete as success.
    [HttpPost, Route("{id:long}/delete", Name = "CompanyDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _companies.SoftDelete(id); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        return Ok();
    }

    // Feeds the "linked sources" popup shown when a delete is blocked. Returns both directions:
    // "owned" = this company's own sources (these block deletion); "inverse" = sources owned by
    // OTHER companies that point at this one. Both are soft-deleted via the source APIs from the UI.
    [AllowAnonymous]
    [HttpGet, Route("{id:long}/linked-sources")]
    public IActionResult LinkedSources(long id)
    {
        var c = _companies.GetWithGraphRelations(id);
        if (c == null) return NotFound();

        var owned = c.RevenueSources.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved)
                .Select(r => new { kind = "revenue", direction = "owned", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.RelatedCompany?.Name })
            .Concat(c.CostSources.Where(s => s.DeletedAt == null && s.Status == ContributionStatus.Approved)
                .Select(s => new { kind = "cost", direction = "owned", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.RelatedCompany?.Name }));

        var inverse = c.RevenueFromDependents.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved)
                .Select(r => new { kind = "revenue", direction = "inverse", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.Company?.Name })
            .Concat(c.CostFromDependents.Where(s => s.DeletedAt == null && s.Status == ContributionStatus.Approved)
                .Select(s => new { kind = "cost", direction = "inverse", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.Company?.Name }));

        return Json(new { owned, inverse });
    }

    private void PopulateDropdowns()
    {
        ViewBag.Sectors = Enum.GetValues<Sector>()
            .Select(s => new SelectListItem(s.ToString().Replace("_", " "), s.ToString())).ToList();
        ViewBag.Industries = Enum.GetValues<GicsIndustry>()
            .Select(i => new SelectListItem(i.ToString().Replace("_", " "), i.ToString())).ToList();
    }

    private static CompanyEditModel ToEditModel(Company c) => new()
    {
        Id = c.Id,
        Name = c.Name,
        Cik = c.Cik,
        Type = c.Type,
        CountryId = c.CountryId,
        Sector = c.Sector,
        Industry = c.Industry,
        RevenueTotal = c.RevenueTotal,
        GrossMargin = c.GrossMargin,
        MarketCap = c.MarketCap,
        AsOf = c.AsOf,
        Notes = c.Notes
    };

    private static void ApplyEdit(Company c, CompanyEditModel m)
    {
        c.Name = m.Name;
        c.Cik = m.Cik;
        c.Type = m.Type;
        c.CountryId = m.CountryId;
        c.Sector = m.Sector;
        c.Industry = m.Industry;
        c.RevenueTotal = m.RevenueTotal;
        c.GrossMargin = m.GrossMargin;
        c.MarketCap = m.MarketCap;
        c.AsOf = m.AsOf;
        c.Notes = m.Notes;
    }
}
