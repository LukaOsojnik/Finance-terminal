using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// Phase-1 extraction UI (revenue only, for now): a split screen whose right pane is the JSON
/// returned by <c>POST /api/stock/refresh/{companyId}</c> and whose left cells bind to a
/// <see cref="RevenueSource"/> row. Saving freezes the row's proof onto the row itself: one
/// Reference (where in the document), one Evidence (the verbatim quote), and the source Filing.
/// </summary>
[Route("extraction")]
// Any authenticated user — the keyed features run on the USER's own API keys (bring-your-own), so a
// signed-in customer can use them; no Admin/Manager role required. A missing key surfaces the
// "add your key" popup; logged-out callers get the sign-in prompt.
[Authorize]
public class ExtractionController : Controller
{
    private readonly IRevenueSourceRepository _revenue;
    private readonly ICostSourceRepository _cost;
    private readonly ICompanyRiskRepository _risks;
    private readonly ICompanyRepository _companies;
    private readonly IFilingRepository _filings;
    private readonly IFilingExtractionService _extractor;
    private readonly ICounterpartyDiscovery _discovery;
    private readonly IContributionWriter _writer;
    private readonly ICompanyProvisioningService _provisioning;
    private readonly ScanJobStore _jobs;
    private readonly RediscoverJobStore _rediscoverJobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserApiKeyProvider _keys;

    public ExtractionController(
        IRevenueSourceRepository revenue,
        ICostSourceRepository cost,
        ICompanyRiskRepository risks,
        ICompanyRepository companies,
        IFilingRepository filings,
        IFilingExtractionService extractor,
        ICounterpartyDiscovery discovery,
        IContributionWriter writer,
        ICompanyProvisioningService provisioning,
        ScanJobStore jobs,
        RediscoverJobStore rediscoverJobs,
        IServiceScopeFactory scopeFactory,
        IUserApiKeyProvider keys)
    {
        _revenue = revenue;
        _cost = cost;
        _risks = risks;
        _companies = companies;
        _filings = filings;
        _extractor = extractor;
        _discovery = discovery;
        _writer = writer;
        _provisioning = provisioning;
        _jobs = jobs;
        _rediscoverJobs = rediscoverJobs;
        _scopeFactory = scopeFactory;
        _keys = keys;
    }

    // Write the same 424 "missing key" envelope the global filter produces, for STREAMING actions
    // that have already begun writing (the filter can't replace a started response). site.js reads
    // {code:"MISSING_KEY", …} off a 424 and shows the "add your key" popup.
    private async Task WriteMissingKeyAsync(MissingApiKeyException ex, CancellationToken ct)
    {
        Response.StatusCode = StatusCodes.Status424FailedDependency;
        Response.ContentType = "application/json; charset=utf-8";
        await Response.WriteAsync(
            JsonSerializer.Serialize(new { code = "MISSING_KEY", provider = ex.Provider, message = ex.Message }), ct);
    }

    private static ExtractionNode ParseNode(string? node) =>
        Enum.TryParse<ExtractionNode>(node, true, out var n) ? n : ExtractionNode.REVENUE;

    // The AI actions below all run through IChatLlm, which routes on the user's stored
    // ParsingProvider — so the key to verify up front is THAT provider's, not DeepSeek's. Checking
    // DeepSeek unconditionally blocked anyone who had picked Kimi/OpenAI/Anthropic from ever starting
    // a scan, however valid their own key was. Throwing here (before a ScanJob is registered) is what
    // turns a missing key into the add-your-key popup instead of a job that dies in the background.
    private static void RequireParsingKey(UserApiKeys keys)
    {
        if (string.IsNullOrWhiteSpace(keys.KeyFor(keys.ParsingProvider)))
            throw MissingApiKeyException.ForParsingProvider(keys.ParsingProvider);
    }

    // Contribution gate: a Manager/Admin's writes go live (Approved); everyone else's are held as
    // Pending contributions stamped with the contributor, for a Manager to review. (UpsertRowByNode
    // is the single chokepoint every web-searched/LLM-parsed revenue/cost/risk row flows through.)
    private bool IsReviewer => User.IsInRole("Admin") || User.IsInRole("Manager");
    private string? CurrentUserId => User.FindFirstValue(ClaimTypes.NameIdentifier);
    // The contributor context the writer needs to apply the reviewer-gate (live vs pending + stamp).
    private Contributor By => new(IsReviewer, CurrentUserId);

    [HttpGet, Route("")]
    public IActionResult Index(long? companyId, long? revenueSourceId, string? node)
    {
        var parsedNode = ParseNode(node);
        var vm = new ExtractionIndexViewModel { CompanyId = companyId, Node = parsedNode };

        // Opened from a source's Details "Add references": prefill that row's values so the user
        // browses EDGAR against the existing source instead of a blank new row. (Revenue only — the
        // deep-link comes from a RevenueSource.)
        if (parsedNode == ExtractionNode.REVENUE && revenueSourceId is { } rowId && _revenue.GetById(rowId) is { } row)
        {
            vm.RevenueSourceId = row.Id;
            vm.CompanyId = row.CompanyId;
            vm.SourceType = row.SourceType;
            vm.Name = row.Name;
            vm.Value = row.Value;
            vm.Percentage = row.Percentage;
            vm.RelatedCompanyId = row.RelatedCompanyId;
            vm.RelatedCompanyLabel = row.RelatedCompany?.Name;
        }

        if (vm.CompanyId is { } id)
            vm.CompanyLabel = _companies.GetById(id)?.Name;

        // Classification option lists per node — the page swaps the dropdown when the node changes.
        ViewBag.SourceTypes = EnumSelect.Of<SourceType>();
        ViewBag.Nodes = EnumSelect.Of<ExtractionNode>();
        // Classification options the page swaps between when the node changes.
        ViewBag.ClassOptions = new Dictionary<string, string[]>
        {
            ["REVENUE"] = Enum.GetNames<SourceType>(),
            ["COST"] = Enum.GetNames<CostBase>(),
            ["RISK"] = Enum.GetNames<RiskScope>(),
        };
        return View(vm);
    }

    // The proof already on a source row, so the page can show it when it binds the row.
    [HttpGet, Route("references/{sourceId:long}")]
    public IActionResult References(long sourceId, [FromQuery] string? node)
    {
        var proof = ParseNode(node) switch
        {
            ExtractionNode.COST => _cost.GetById(sourceId) is { } c ? Dto(c.Reference, c.Evidence, c.Filing) : null,
            ExtractionNode.RISK => _risks.GetById(sourceId) is { } k ? Dto(k.Reference, k.Evidence, k.Filing) : null,
            _                   => _revenue.GetById(sourceId) is { } r ? Dto(r.Reference, r.Evidence, r.Filing) : null,
        };
        if (proof is null) return NotFound();
        return Json(proof);

        static object Dto(string? reference, string? evidence, Filing? filing) => new
        {
            reference,
            evidence,
            filing = filing is null || filing.DeletedAt != null
                ? null
                : $"{filing.Form} {filing.AccessionNumber}".Trim()
        };
    }

    // Set this row's reference + evidence (and the filing they came from), creating the row when the
    // page hasn't bound one yet.
    [HttpPost, Route("reference")]
    public IActionResult Reference([FromBody] ReferenceRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
        if (string.IsNullOrWhiteSpace(req.Evidence)) return BadRequest("Select proof text first.");
        var node = ParseNode(req.Node);

        // Proof filing: upsert by accession when the proof came from a filing document; null from
        // Company Facts (the row then carries the quote without a filing link).
        var filingId = ResolveFilingId(req.CompanyId, req.FilingAccessionNumber, req.FilingForm, req.FilingDate, req.FilingUrl);

        var rowId = _writer.UpsertRow(node, req.CompanyId, req.RevenueSourceId, req.SourceType,
            req.Name, req.Value, req.Percentage, req.Note, req.RelatedCompanyId, By,
            req.Reference, req.Evidence, filingId);
        if (rowId is null) return BadRequest("Could not save the row (check the classification value).");

        return Json(new ReferenceResult(rowId.Value));
    }

    // One button to save the whole form: the row's values plus its single reference + evidence.
    [HttpPost, Route("save")]
    public IActionResult Save([FromBody] SaveRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
        var node = ParseNode(req.Node);

        var filingId = ResolveFilingId(req.CompanyId, req.FilingAccessionNumber, req.FilingForm, req.FilingDate, req.FilingUrl);
        var rowId = _writer.UpsertRow(node, req.CompanyId, req.RevenueSourceId, req.SourceType,
            req.Name, req.Value, req.Percentage, req.Note, req.RelatedCompanyId, By,
            req.Reference, req.Evidence, filingId);
        if (rowId is null) return BadRequest("Could not save the row (check the classification value).");

        return Json(new { revenueSourceId = rowId.Value, proof = !string.IsNullOrWhiteSpace(req.Evidence) });
    }

    // Batch save from the notification widget's chat: persist every ticked AI ```save``` block in one
    // call. Each item upserts its source row (proof included); items naming a counterparty resolve
    // (or create via the FMP/Yahoo pipeline) that company and get a reciprocal mirror row — so the
    // relationship is saved bidirectionally, the same way the discover→link flow does it.
    [HttpPost, Route("save-batch")]
    public async Task<IActionResult> SaveBatch([FromBody] SaveBatchRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        var owner = _companies.GetById(req.CompanyId);
        if (owner is null) return NotFound();
        var node = ParseNode(req.Node);

        // The filing every item in this batch was read from (upserted once by accession). The URL is
        // rebuilt here from the owner's CIK + the document the scan read, rather than trusted from the
        // client: without it the Filing row is saved with a null PrimaryDocUrl and the proof can never
        // be reopened, which is exactly what happened to every row saved from the chat widget.
        var filingId = ResolveFilingId(req.CompanyId, req.Accession, req.Form, null,
            EdgarArchive.DocUrl(owner.Cik, req.Accession, req.Doc));

        var saved = 0;
        var links = 0;
        // Bare stubs phase 1 created; phase 2 fills them in after the response goes out.
        var stubs = new List<(long CompanyId, string? Ticker)>();
        foreach (var item in req.Items ?? [])
        {
            if (string.IsNullOrWhiteSpace(item.Name)) continue;

            // Counterparty objects (revenue customer / cost supplier) resolve like discover→link.
            var hasCounterparty = node != ExtractionNode.RISK && !string.IsNullOrWhiteSpace(item.RelatedCompany);
            long? counterpartyId = null;
            if (hasCounterparty)
            {
                var linkReq = new LinkCounterpartyRequest
                {
                    CompanyId = req.CompanyId,
                    Name = item.RelatedCompany!.Trim(),
                    Side = node == ExtractionNode.COST ? "SUPPLIER" : "CUSTOMER",
                    Classification = item.Classification,
                    Ticker = item.RelatedCompanyTicker,
                    Value = item.Value
                };
                // Phase 1 only: a name match, else a bare stub. No FMP/Yahoo/sonar/LLM on this path.
                var (cpId, created) = _provisioning.GetOrCreateCounterpartyFast(linkReq, owner);
                counterpartyId = cpId;
                // Only a FRESH stub needs enriching — a name match is an already-populated company.
                if (created) stubs.Add((cpId, item.RelatedCompanyTicker));
            }

            // The block's Reference (where in the filing) and Evidence (the one verbatim quote) ride
            // along onto the row itself, together with the filing they were read from.
            var rowId = _writer.UpsertRow(node, req.CompanyId, null, item.Classification, item.Name,
                item.Value, item.Percentage, item.Note, counterpartyId, By,
                item.Reference, item.Evidence, filingId);
            if (rowId is null) continue;   // unparseable classification — skip this item
            saved++;

            if (hasCounterparty && counterpartyId is { } cid)
            {
                _writer.EnsureReciprocal(node, cid, req.CompanyId, owner.Name, item.Value, By);
                links++;
            }
        }
        // Phase 2, detached: fill the stubs we just created. The response goes out first, so the user
        // sees the rows land now and the counterparty cards fill in a moment later.
        if (stubs.Count > 0) QueueCounterpartyEnrichment(stubs, await _keys.GetAsync());

        return Json(new { saved, links });
    }

    // Phase 2 of save-batch: enrich the bare counterparty stubs phase 1 wrote — FMP + financial history
    // for the ticker'd ones, Perplexity sonar profile discovery for the private ones. Runs on a DETACHED
    // scope for the same reason CompaniesController.Rediscover does: the request-scoped DbContext is
    // disposed the moment this action returns, and the vendor + LLM calls take far longer than the save.
    // Fire-and-forget on purpose — the rows are already saved and linked, nothing in the UI waits on
    // this, and EnrichCounterpartiesAsync logs its own per-stub failures.
    private void QueueCounterpartyEnrichment(
        IReadOnlyList<(long CompanyId, string? Ticker)> stubs, UserApiKeys keys)
    {
        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            // The keyed calls run on the USER's own keys, which live on HttpContext — hence the snapshot.
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            await sp.GetRequiredService<ICompanyProvisioningService>().EnrichCounterpartiesAsync(stubs);
        });
    }

    // Mode B — AI reads one filing and proposes revenue rows + their proof for the human to
    // confirm. Persists nothing; the page fills the form and the existing save path freezes proof.
    [HttpPost, Route("auto-extract/{companyId:long}")]
    public async Task<IActionResult> AutoExtract(long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromQuery] string? node, [FromQuery] string? form)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            var suggestions = await _extractor.ExtractAsync(companyId, accession, doc, ParseNode(node), form);
            return Json(suggestions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek unreachable.");
        }
    }

    // Mode B (auto) — triage every bold heading by title, scan the AI-chosen ones in parallel, and
    // stash the result as the chat's grounding. Replaces the hand-pick flow. Returns scanned + found.
    [HttpPost, Route("scan-auto/{companyId:long}")]
    public async Task<IActionResult> ScanAuto(
        long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromQuery] string? node, [FromQuery] string? form)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            var result = await _extractor.ScanAutoAsync(companyId, accession, doc, ParseNode(node), form);
            return Json(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek/SEC unreachable.");
        }
    }

    // Mode B (async) — same scan as scan-auto, but detached: register a job, fire the work on a
    // background task, and return its id at once so the page doesn't block. The user can navigate
    // away; the notification widget polls scan-jobs for the result. The background task opens its
    // OWN DI scope — the request scope (and its DbContext) is gone the moment this returns.
    [HttpPost, Route("scan-auto-async/{companyId:long}")]
    public async Task<IActionResult> ScanAutoAsync(
        long companyId, [FromQuery] string accession, [FromQuery] string doc,
        [FromQuery] string? node, [FromQuery] string? form,
        [FromQuery] string? companyName, [FromQuery] string? filingLabel)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");

        // The scan + summary run on the user's chosen parsing provider. Verify that key now (throw ->
        // 424 popup) and snapshot the keys so the detached background scope can use them (no HttpContext).
        var keys = await _keys.GetAsync();
        RequireParsingKey(keys);

        var parsedNode = ParseNode(node);
        var job = new ScanJob
        {
            CompanyId = companyId,
            CompanyName = companyName ?? _companies.GetById(companyId)?.Name ?? "",
            Accession = accession,
            Doc = doc,
            Node = parsedNode.ToString(),
            Form = form,
            FilingLabel = filingLabel ?? form ?? "filing"
        };
        // Prefill the section boxes from the node's known SEC Items so the widget shows the layout
        // immediately (spinning) while triage/fetch run. The Planned event replaces these with the
        // real per-Item chunk rows once the scan has decided what to read.
        foreach (var item in FilingSections.ItemsFor(parsedNode, form))
            job.Sections.Add(new ScanSection { Item = $"Item {item}" });
        _jobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var extractor = scope.ServiceProvider.GetRequiredService<IFilingExtractionService>();
            var chat = scope.ServiceProvider.GetRequiredService<IExtractionChatService>();
            try
            {
                // Coarse phase text for the pre-triage window; once chunks are planned the widget shows
                // the live section tree instead (filled by the progress callback below).
                job.Progress = $"Reading the {job.FilingLabel} & triaging sections with parallel agents…";
                job.Report = await extractor.ScanAutoAsync(
                    companyId, accession, doc, parsedNode, form, p => ApplyScanProgress(job, p));
                // The audited tagged XBRL facts (COST/REVENUE; null for RISK) for the widget's table —
                // also primes the cache the summary turn below grounds on, so it's one SEC round-trip.
                job.Xbrl = await chat.GetXbrlViewAsync(companyId, accession, parsedNode);
                // Auto AI summary: one chat turn grounded on the digest the scan just cached, so the
                // notification opens with a real answer rather than just counts.
                job.Progress = $"Found {job.Report.Found} candidate(s) · writing summary…";
                var seed = new List<ChatMessage>
                {
                    new("user", "Summarize the candidates you found in this filing.")
                };
                // Stream the first answer into the SAME live buffers the follow-up replies use, so the
                // widget shows the summary generating token-by-token (with its reasoning trace) instead
                // of the whole thing appearing at once when the job flips to Done.
                job.Replying = true;
                job.ReplyBuffer = "";
                job.ReplyThink = "";
                await foreach (var d in chat.StreamReplyAsync(companyId, accession, doc, parsedNode, seed, form))
                {
                    if (d.Kind == "text") job.ReplyBuffer += d.Text;
                    else if (d.Kind == "reasoning") job.ReplyThink += d.Text;
                }
                job.Summary = job.ReplyBuffer;
                job.Replying = false;
                job.Progress = "";
                job.Status = ScanJobStatus.Done;
            }
            catch (Exception ex)
            {
                job.Status = ScanJobStatus.Error;
                job.Error = ex.Message;
                job.Progress = "";
                job.Replying = false;   // a crash mid-summary must not leave the widget "replying" forever
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        });

        return Json(new { jobId = job.Id });
    }

    // Cross-segment hand-off (worker-less). The SOURCE segment's agent already FOUND the fact and
    // emitted a ```handoff``` block; this spins up the TARGET segment's agent to re-dress it in that
    // segment's save schema — NO worker re-scan (scanIfMissing:false grounds on whatever's cached + the
    // seed + the cheap XBRL view). It runs one turn whose first user message IS the seed, so the target
    // widget opens with a proposed ```save``` block ready to tick + Save. See docs/extraction/cross-extraction.md.
    [HttpPost, Route("scan-handoff/{companyId:long}")]
    public async Task<IActionResult> ScanHandoff(
        long companyId, [FromBody] ScanHandoffRequest req,
        [FromQuery] string accession, [FromQuery] string doc,
        [FromQuery] string? node, [FromQuery] string? form,
        [FromQuery] string? companyName, [FromQuery] string? filingLabel)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        if (req is null || string.IsNullOrWhiteSpace(req.Seed)) return BadRequest("seed is required.");

        // Runs on the user's chosen parsing provider. Verify that key now (throw -> 424 popup) and
        // snapshot it for the detached scope (no HttpContext there).
        var keys = await _keys.GetAsync();
        RequireParsingKey(keys);

        var parsedNode = ParseNode(node);
        var seed = req.Seed;
        var job = new ScanJob
        {
            CompanyId = companyId,
            CompanyName = companyName ?? _companies.GetById(companyId)?.Name ?? "",
            Accession = accession,
            Doc = doc,
            Node = parsedNode.ToString(),
            Form = form,
            FilingLabel = filingLabel ?? form ?? "filing"
        };
        _jobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var chat = scope.ServiceProvider.GetRequiredService<IExtractionChatService>();
            try
            {
                // No ScanAutoAsync — the source agent already found this; we only re-dress it in this
                // segment's schema. The XBRL view is a cheap cached read (audited figures if tagged).
                job.Xbrl = await chat.GetXbrlViewAsync(companyId, accession, parsedNode);
                job.Progress = "Recording the handed-over item…";
                job.Replying = true;
                job.ReplyBuffer = "";
                job.ReplyThink = "";
                // The seed IS the first turn (not the canned "summarize the candidates"); scanIfMissing
                // :false keeps the workers off.
                var history = new List<ChatMessage> { new("user", seed) };
                await foreach (var d in chat.StreamReplyAsync(
                    companyId, accession, doc, parsedNode, history, form, handoff: true))
                {
                    if (d.Kind == "text") job.ReplyBuffer += d.Text;
                    else if (d.Kind == "reasoning") job.ReplyThink += d.Text;
                }
                job.Summary = job.ReplyBuffer;   // lets ensureChatHistory reconstruct the assistant turn
                job.Replying = false;
                job.Progress = "";
                job.Status = ScanJobStatus.Done;
            }
            catch (Exception ex)
            {
                job.Status = ScanJobStatus.Error;
                job.Error = ex.Message;
                job.Progress = "";
                job.Replying = false;
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        });

        return Json(new { jobId = job.Id });
    }

    // Status of the jobs the browser is tracking (ids it holds in localStorage, comma-separated).
    // Unknown ids are skipped — the store evicts nothing, but a dismissed/lost job just drops out.
    [HttpGet, Route("scan-jobs")]
    public IActionResult ScanJobs([FromQuery] string? ids)
    {
        // The browser tracks both filing-scan and private-company re-discovery job ids in one list;
        // resolve each against whichever store holds it. Both shapes carry a `kind` so the widget can
        // tell a chat-capable scan from a fire-and-forget re-discovery.
        var list = (ids ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(id => _jobs.Get(id) is { } s ? ScanDto(s)
                        : _rediscoverJobs.Get(id) is { } r ? RediscoverDto(r)
                        : null)
            .Where(j => j is not null);
        return Json(list);
    }

    // Fold one scan progress event into the job's live section tree. Called from concurrent worker
    // threads, so all mutation is under the job's lock; ScanDto snapshots under the same lock.
    private static void ApplyScanProgress(ScanJob job, ScanProgress p)
    {
        lock (job.SectionsLock)
        {
            if (p.Phase == ScanChunkPhase.Planned)
            {
                job.Sections.Clear();
                job.ChunkList.Clear();
                foreach (var info in p.Plan ?? [])
                {
                    var state = new ScanChunkState { Titles = info.Titles };
                    job.ChunkList.Add(state);   // index-aligned with info.Index
                    var section = job.Sections.FirstOrDefault(s => s.Item == info.Item);
                    if (section is null) { section = new ScanSection { Item = info.Item }; job.Sections.Add(section); }
                    section.Chunks.Add(state);
                }
            }
            else if (p.Index >= 0 && p.Index < job.ChunkList.Count)
            {
                var state = job.ChunkList[p.Index];
                state.Status = p.Phase.ToString();
                if (p.Phase == ScanChunkPhase.Done) state.Found = p.Found;
                // Stash the verbatim prompt + reply for the widget's per-chunk inspector (Done/Error only).
                if (p.Prompt != null) state.Prompt = p.Prompt;
                if (p.Response != null) state.Response = p.Response;
            }
        }
    }

    private static object ScanDto(ScanJob j)
    {
        // Snapshot the live section tree under the same lock the worker threads write it with.
        object[] sections;
        lock (j.SectionsLock)
            sections = j.Sections.Select(s => (object)new
            {
                item = s.Item,
                // idx is the chunk's position in the flat ChunkList — the key the inspector endpoint
                // takes; hasDetail flags that the prompt/reply are captured (chunk finished), so the
                // widget only makes finished rows clickable.
                chunks = s.Chunks.Select(c => new
                {
                    titles = c.Titles, status = c.Status, found = c.Found,
                    idx = j.ChunkList.IndexOf(c), hasDetail = c.Prompt.Length > 0
                }).ToArray()
            }).ToArray();

        return new
        {
            kind = "scan",
            id = j.Id,
            status = j.Status.ToString(),
            progress = j.Progress,
            replying = j.Replying,
            createdAt = j.CreatedAt,
            companyId = j.CompanyId,
            companyName = j.CompanyName,
            accession = j.Accession,
            doc = j.Doc,
            node = j.Node,
            form = j.Form,
            filingLabel = j.FilingLabel,
            found = j.Report?.Found ?? 0,
            sections,
            xbrl = XbrlDto(j.Xbrl),
            summary = j.Summary,
            error = j.Error
        };
    }

    // Project the structured XBRL view into the camelCase shape the widget's xbrlBox() reads. Null when
    // the node is RISK or the filing tagged nothing — the widget then renders no XBRL box.
    private static object? XbrlDto(XbrlView? x) => x is null ? null : new
    {
        node = x.Node,
        periodEnd = x.PeriodEnd,
        totals = x.Totals.Select(t => new { label = t.Label, value = t.Value }).ToArray(),
        segments = x.Segments.Select(s => new { segment = s.Segment, value = s.Value, detail = s.Detail, reconciles = s.Reconciles }).ToArray(),
        sumCheck = x.SumCheck is { } c ? new { segmentSum = c.SegmentSum, total = c.Total, ties = c.Ties } : null
    };

    // Project a re-discovery job into the same shape the widget consumes, with the chat-only fields
    // blanked. filingLabel/node fill the widget's title slots with a sensible label.
    private static object RediscoverDto(RediscoverJob j) => new
    {
        kind = "rediscover",
        id = j.Id,
        status = j.Status.ToString(),
        progress = j.Progress,
        replying = false,
        createdAt = j.CreatedAt,
        companyId = j.CompanyId,
        companyName = j.CompanyName,
        accession = "",
        doc = "",
        node = "PROFILE",
        form = (string?)null,
        filingLabel = "Profile re-discovery",
        found = 0,
        summary = "",
        result = j.Result,
        proposed = j.Proposed != null && !j.Applied,   // awaiting the user's accept/reject
        applied = j.Applied,
        sources = j.Sources,
        error = j.Error
    };

    // Inspector: the verbatim prompt one worker agent saw + its raw reply. Fetched lazily when the
    // user expands a chunk row, so the heavy excerpt text never rides the 2s status poll. `index` is
    // the chunk's position in the flat ChunkList (the `idx` the status DTO hands the widget).
    [HttpGet, Route("scan-jobs/{jobId}/chunk/{index:int}")]
    public IActionResult ScanJobChunk(string jobId, int index)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return NotFound();
        lock (job.SectionsLock)
        {
            if (index < 0 || index >= job.ChunkList.Count) return NotFound();
            var c = job.ChunkList[index];
            return Json(new { titles = c.Titles, status = c.Status, found = c.Found, prompt = c.Prompt, response = c.Response });
        }
    }

    // Drop a job the user dismissed from the widget. Try both stores — the id is from either.
    [HttpPost, Route("scan-jobs/dismiss/{jobId}")]
    public IActionResult DismissScanJob(string jobId)
    {
        _jobs.Remove(jobId);
        _rediscoverJobs.Remove(jobId);
        return Ok();
    }

    // Start a detached follow-up chat reply for a finished scan: generate on a background task so
    // the answer survives the user navigating away. The widget POSTs the conversation so far, then
    // polls scan-jobs/{id}/reply for the streamed result. Reuses the existing chat grounding.
    [HttpPost, Route("scan-jobs/{jobId}/reply")]
    public async Task<IActionResult> ScanJobReply(string jobId, [FromBody] ScanJobReplyRequest req)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return NotFound();
        if (job.Status != ScanJobStatus.Done) return BadRequest("Scan hasn't finished.");
        if (job.Replying) return Conflict("A reply is already in progress.");

        // The reply streams via the user's chosen parsing provider. Verify that key now (throw -> 424
        // popup) and snapshot the keys so the detached background scope can use them (no HttpContext).
        var keys = await _keys.GetAsync();
        RequireParsingKey(keys);

        var node = ParseNode(job.Node);
        var history = req?.Messages ?? [];
        var handoff = req?.Handoff ?? false;
        job.Replying = true;
        job.ReplyBuffer = "";
        job.ReplyThink = "";
        job.ReplyError = null;

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            scope.ServiceProvider.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var chat = scope.ServiceProvider.GetRequiredService<IExtractionChatService>();
            try
            {
                await foreach (var d in chat.StreamReplyAsync(job.CompanyId, job.Accession, job.Doc, node, history, job.Form, handoff))
                {
                    if (d.Kind == "text") job.ReplyBuffer += d.Text;
                    else if (d.Kind == "reasoning") job.ReplyThink += d.Text;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                job.ReplyError = "DeepSeek unreachable.";
            }
            catch (Exception ex)
            {
                job.ReplyError = ex.Message;
            }
            finally
            {
                job.Replying = false;
            }
        });

        return Ok();
    }

    // Poll the in-flight (or just-finished) reply for a job.
    [HttpGet, Route("scan-jobs/{jobId}/reply")]
    public IActionResult ScanJobReplyState(string jobId)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return NotFound();
        return Json(new { replying = job.Replying, reply = job.ReplyBuffer, think = job.ReplyThink, error = job.ReplyError });
    }

    // Web discovery — ask Perplexity sonar for the named counterparties behind the company's revenue
    // (Side=CUSTOMER) or cost (Side=SUPPLIER) segments. Runs Perplexity-style: a planner decomposes the
    // request into focused sub-queries, each its own grounded search. Streams NDJSON lines as it goes
    // ({"t":"plan"|"searching"|"result"|"error", …}) so the page renders a live feed. Persists nothing;
    // the user confirms each found counterparty via LinkCounterparty.
    [HttpPost, Route("discover-related")]
    public async Task DiscoverRelated([FromBody] DiscoverCounterpartiesRequest req, CancellationToken ct)
    {
        if (req is null || req.CompanyId <= 0 || _companies.GetById(req.CompanyId) is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        // Discovery streams via Perplexity — verify the user's key BEFORE the NDJSON body starts.
        if (string.IsNullOrWhiteSpace((await _keys.GetAsync(ct)).Perplexity))
        {
            await WriteMissingKeyAsync(MissingApiKeyException.Perplexity(), ct);
            return;
        }

        Response.ContentType = "application/x-ndjson; charset=utf-8";
        // Flush headers NOW, before the planner LLM call. Otherwise the response stays uncommitted until
        // the first "plan" event (seconds later), so the client's `await fetch` doesn't resolve and the
        // button shows no "Planning searches…" feedback. The 424 key-check above already returned, so
        // committing here is safe.
        await Response.Body.FlushAsync(ct);
        var side = string.Equals(req.Side, "SUPPLIER", StringComparison.OrdinalIgnoreCase) ? "SUPPLIER" : "CUSTOMER";
        // Empty segments is allowed: the planner then identifies the company's segments itself (so
        // discovery works on a company that has no sources on record yet).
        var segments = (req.Segments ?? [])
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // Web options (camelCase) so the streamed items match what the page reads (s.name, s.sourceUrl…).
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        try
        {
            await foreach (var e in _discovery.DiscoverAsync(req.CompanyId, side, segments, req.Valued, ct))
            {
                object line = e.Type switch
                {
                    "plan" => new { t = "plan", queries = e.Queries },
                    "searching" => new { t = "searching", query = e.Query },
                    "result" => new { t = "result", query = e.Query, items = e.Items, sources = e.Sources, error = e.Error },
                    _ => new { t = e.Type }
                };
                await Response.WriteAsync(JsonSerializer.Serialize(line, json) + "\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(new { t = "error", c = "Web search unreachable." }) + "\n", ct);
        }
    }

    // Confirm one discovered counterparty: resolve (or create) its Company row, then create a revenue
    // source (CUSTOMER) or cost source (SUPPLIER) on the inspected company pointing at it — feeding the
    // graph's RELATED COMPANIES hub via RelatedCompanyId. Value is null (web gives no figure); the row
    // exists to carry the relationship.
    [HttpPost, Route("link-counterparty")]
    public async Task<IActionResult> LinkCounterparty([FromBody] LinkCounterpartyRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Counterparty name required.");
        var owner = _companies.GetById(req.CompanyId);
        if (owner is null) return NotFound();

        var counterpartyId = req.ExistingCompanyId ?? await _provisioning.GetOrCreateCounterpartyAsync(req, owner);

        // CUSTOMER buys from us -> revenue source; SUPPLIER sells to us -> cost source.
        var node = string.Equals(req.Side, "SUPPLIER", StringComparison.OrdinalIgnoreCase)
            ? ExtractionNode.COST
            : ExtractionNode.REVENUE;
        // Sonar's citation URL is this row's Reference (where the claim came from) and its one-line
        // note the Evidence, so the web source behind the relationship is recorded on the row (and
        // shown on the company's Details page). No filing — this came from the web, not a filing.
        var rowId = _writer.UpsertRow(node, req.CompanyId, null, req.Classification, req.Name,
            value: req.Value, percentage: null, note: null, relatedCompanyId: counterpartyId, By,
            reference: req.SourceUrl, evidence: req.Note);
        if (rowId is null) return BadRequest("Could not create the link (check the classification value).");

        // The relationship is symmetric but stored as two one-sided rows: owner gets a row pointing at
        // the counterparty (above); the counterparty needs the mirror row pointing back at owner, or its
        // Details page shows nothing. Owner's revenue (counterparty is its CUSTOMER) -> counterparty's
        // cost (owner is its supplier, COGS); owner's cost (counterparty is its supplier) ->
        // counterparty's revenue (owner is its CUSTOMER).
        _writer.EnsureReciprocal(node, counterpartyId, req.CompanyId, owner.Name, req.Value, By);

        return Json(new { sourceId = rowId.Value, counterpartyId, node = node.ToString() });
    }

    // Upsert the proof filing by accession (globally unique) and return its id. Returns null when
    // no filing was open (proof came from Company Facts) so the caller leaves the link untouched.
    private long? ResolveFilingId(long companyId, string? accession, string? form, string? date, string? url)
    {
        if (string.IsNullOrWhiteSpace(accession)) return null;

        DateTime? filingDate = DateTime.TryParse(
            date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

        // Upsert by accession (revives a soft-deleted row instead of a duplicate-insert).
        return _filings.Upsert(companyId, accession, form, filingDate, url).Id;
    }
}
