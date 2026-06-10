using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// Phase-1 extraction UI (revenue only, for now): a split screen whose right pane is the JSON
/// returned by <c>POST /api/stock/refresh/{companyId}</c> and whose left cells bind to a
/// <see cref="RevenueSource"/> row. "Use as reference" freezes the selected proof text into a
/// <see cref="SourceFieldReview"/> (one per cell, <c>Mark=null</c> for the phase-2 reviewer).
/// </summary>
[Route("extraction")]
public class ExtractionController : Controller
{
    private readonly IRevenueSourceRepository _revenue;
    private readonly ICostSourceRepository _cost;
    private readonly ICompanyRiskRepository _risks;
    private readonly ISourceFieldReviewRepository _reviews;
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;
    private readonly IFilingRepository _filings;
    private readonly IReviewService _reviewer;
    private readonly IFilingExtractionService _extractor;
    private readonly IExtractionChatService _chat;
    private readonly ICounterpartyDiscovery _discovery;
    private readonly IFmpApiClient _fmp;
    private readonly IYahooFinanceClient _yahoo;
    private readonly IExchangeRateApiClient _exchangeRate;
    private readonly ICompanyFinancialsService _financials;
    private readonly IIndustryClassifier _industryClassifier;
    private readonly ScanJobStore _jobs;
    private readonly RediscoverJobStore _rediscoverJobs;
    private readonly IServiceScopeFactory _scopeFactory;

    public ExtractionController(
        IRevenueSourceRepository revenue,
        ICostSourceRepository cost,
        ICompanyRiskRepository risks,
        ISourceFieldReviewRepository reviews,
        ICompanyRepository companies,
        ICountryRepository countries,
        IFilingRepository filings,
        IReviewService reviewer,
        IFilingExtractionService extractor,
        IExtractionChatService chat,
        ICounterpartyDiscovery discovery,
        IFmpApiClient fmp,
        IYahooFinanceClient yahoo,
        IExchangeRateApiClient exchangeRate,
        ICompanyFinancialsService financials,
        IIndustryClassifier industryClassifier,
        ScanJobStore jobs,
        RediscoverJobStore rediscoverJobs,
        IServiceScopeFactory scopeFactory)
    {
        _revenue = revenue;
        _cost = cost;
        _risks = risks;
        _reviews = reviews;
        _companies = companies;
        _countries = countries;
        _filings = filings;
        _reviewer = reviewer;
        _extractor = extractor;
        _chat = chat;
        _discovery = discovery;
        _fmp = fmp;
        _yahoo = yahoo;
        _exchangeRate = exchangeRate;
        _financials = financials;
        _industryClassifier = industryClassifier;
        _jobs = jobs;
        _rediscoverJobs = rediscoverJobs;
        _scopeFactory = scopeFactory;
    }

    private static ExtractionNode ParseNode(string? node) =>
        Enum.TryParse<ExtractionNode>(node, true, out var n) ? n : ExtractionNode.REVENUE;

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
        ViewBag.SourceTypes = EnumItems<SourceType>();
        ViewBag.Nodes = EnumItems<ExtractionNode>();
        // Classification options the page swaps between when the node changes.
        ViewBag.ClassOptions = new Dictionary<string, string[]>
        {
            ["REVENUE"] = Enum.GetNames<SourceType>(),
            ["COST"] = Enum.GetNames<CostBase>(),
            ["RISK"] = Enum.GetNames<RiskScope>(),
        };
        return View(vm);
    }

    private static List<SelectListItem> EnumItems<T>() where T : struct, Enum =>
        Enum.GetValues<T>().Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();

    // Existing references for a source row, so the page can show each cell's pointer on load.
    [HttpGet, Route("references/{sourceId:long}")]
    public IActionResult References(long sourceId, [FromQuery] string? node)
    {
        var n = ParseNode(node);
        var companyId = RowCompanyId(n, sourceId);
        if (companyId is null) return NotFound();
        var refs = _reviews.GetByCompany(companyId.Value)
            .Where(r => MatchesRow(r, n, sourceId))
            .Select(r => new
            {
                field = r.Field.ToString(),
                snapshot = r.ReferenceSnapshot,
                pointer = r.ReferencePointer,
                endpoint = r.Endpoint,
                mark = r.Mark,
                rationale = r.Rationale,
                filing = r.Filing == null ? null : $"{r.Filing.Form} {r.Filing.AccessionNumber}".Trim()
            });
        return Json(refs);
    }

    // "Use as reference": ensure the source row exists (FK ordering), then upsert the per-cell proof.
    [HttpPost, Route("reference")]
    public IActionResult Reference([FromBody] ReferenceRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
        if (string.IsNullOrWhiteSpace(req.ReferenceSnapshot)) return BadRequest("Select proof text first.");
        if (!Enum.TryParse<ReviewableField>(req.Field, out var field)) return BadRequest("Invalid field.");
        var node = ParseNode(req.Node);

        // Proof filing: upsert by accession when the proof came from a filing document; null from
        // Company Facts. Attached per-field, so one source can cite different filings per cell.
        var filingId = ResolveFilingId(req.CompanyId, req.FilingAccessionNumber, req.FilingForm, req.FilingDate, req.FilingUrl);

        // The source row is the source of truth for the values — upsert it first so a review FK can
        // only ever point at a row that exists.
        var rowId = UpsertRowByNode(node, req.CompanyId, req.RevenueSourceId, req.SourceType,
            req.Name, req.Value, req.Percentage, req.Note, req.RelatedCompanyId);
        if (rowId is null) return BadRequest("Could not save the row (check the classification value).");

        var endpoint = string.IsNullOrWhiteSpace(req.Endpoint)
            ? $"POST /api/stock/refresh/{req.CompanyId}"
            : req.Endpoint;
        var review = UpsertReviewByNode(node, req.CompanyId, rowId.Value, field, endpoint,
            req.ReferencePointer, req.ReferenceSnapshot, req.ReferencedValue, filingId);
        return Json(new ReferenceResult(rowId.Value, review.Id, field.ToString()));
    }

    // One button to save the whole form: upsert the row, then upsert a proof per field that carries
    // one. Replaces the old per-cell "Use as reference" flow.
    [HttpPost, Route("save")]
    public IActionResult Save([FromBody] SaveRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
        var node = ParseNode(req.Node);

        var rowId = UpsertRowByNode(node, req.CompanyId, req.RevenueSourceId, req.SourceType,
            req.Name, req.Value, req.Percentage, req.Note, req.RelatedCompanyId);
        if (rowId is null) return BadRequest("Could not save the row (check the classification value).");

        var saved = 0;
        foreach (var p in req.Proofs ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.ReferenceSnapshot)) continue;
            if (!Enum.TryParse<ReviewableField>(p.Field, out var field)) continue;
            var filingId = ResolveFilingId(req.CompanyId, p.FilingAccessionNumber, p.FilingForm, p.FilingDate, p.FilingUrl);
            var endpoint = string.IsNullOrWhiteSpace(p.Endpoint) ? "AI extraction" : p.Endpoint;
            UpsertReviewByNode(node, req.CompanyId, rowId.Value, field, endpoint, p.ReferencePointer, p.ReferenceSnapshot, p.ReferencedValue, filingId);
            saved++;
        }
        return Json(new { revenueSourceId = rowId.Value, proofs = saved });
    }

    // Batch save from the notification widget's chat: persist every ticked AI ```save``` block in one
    // call. Each item upserts its source row + per-field proof; items naming a counterparty resolve
    // (or create via the FMP/Yahoo pipeline) that company and get a reciprocal mirror row — so the
    // relationship is saved bidirectionally, the same way the discover→link flow does it.
    [HttpPost, Route("save-batch")]
    public async Task<IActionResult> SaveBatch([FromBody] SaveBatchRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        var owner = _companies.GetById(req.CompanyId);
        if (owner is null) return NotFound();
        var node = ParseNode(req.Node);

        var saved = 0;
        var links = 0;
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
                counterpartyId = await GetOrCreateCompanyAsync(linkReq, owner);
            }

            var rowId = UpsertRowByNode(node, req.CompanyId, null, item.Classification, item.Name,
                item.Value, item.Percentage, item.Note, counterpartyId);
            if (rowId is null) continue;   // unparseable classification — skip this item
            saved++;

            // Per-field proof: the verbatim filing excerpts the model carried in the save block.
            var filingId = ResolveFilingId(req.CompanyId, req.Accession, req.Form, null, null);
            if (item.Proof is { } p)
            {
                UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.NAME, p.Name, item.Name, filingId);
                UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.VALUE, p.Value, item.Value?.ToString(CultureInfo.InvariantCulture), filingId);
                UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.PERCENTAGE, p.Percentage, item.Percentage?.ToString(CultureInfo.InvariantCulture), filingId);
                UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.CLASSIFICATION, p.Classification, item.Classification, filingId);
                UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.RELATED_COMPANY, p.RelatedCompany, item.RelatedCompany, filingId);
                if (node == ExtractionNode.RISK)
                    UpsertAiProof(node, req.CompanyId, rowId.Value, ReviewableField.NOTE, p.Note, item.Note, filingId);
            }

            if (hasCounterparty && counterpartyId is { } cid)
            {
                EnsureReciprocal(node, cid, req.CompanyId, owner.Name, item.Value);
                links++;
            }
        }
        return Json(new { saved, links });
    }

    // Upsert one AI-suggested field proof, skipping fields the model left without a snapshot.
    private void UpsertAiProof(ExtractionNode node, long companyId, long rowId, ReviewableField field,
        string? snapshot, string? referencedValue, long? filingId)
    {
        if (string.IsNullOrWhiteSpace(snapshot)) return;
        UpsertReviewByNode(node, companyId, rowId, field, "AI extraction", "ai-suggested",
            snapshot, referencedValue, filingId);
    }

    // Create or update the source row for the active node from the form values, returning its id.
    // Returns null when the classification can't be parsed, or an existing-row id pointed at no row.
    // `classification` is the generic string from the form: SourceType / CostBase / RiskScope.
    private long? UpsertRowByNode(
        ExtractionNode node, long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, string? note, long? relatedCompanyId) => node switch
    {
        ExtractionNode.COST => UpsertCost(companyId, rowId, classification, name, value, percentage, relatedCompanyId),
        ExtractionNode.RISK => UpsertRisk(companyId, rowId, classification, name, note),
        _                   => UpsertRevenue(companyId, rowId, classification, name, value, percentage, relatedCompanyId),
    };

    private long? UpsertRevenue(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId)
    {
        if (!Enum.TryParse<SourceType>(classification, out var sourceType)) return null;
        if (rowId is { } id)
        {
            var existing = _revenue.GetById(id);
            if (existing is null) return null;
            existing.SourceType = sourceType;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            _revenue.Update(existing);
            return existing.Id;
        }
        var row = new RevenueSource(sourceType, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            DataSource = DataSource.MANUAL
        };
        _revenue.Add(row);
        return row.Id;
    }

    private long? UpsertCost(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId)
    {
        if (!Enum.TryParse<CostBase>(classification, out var costBase)) return null;
        if (rowId is { } id)
        {
            var existing = _cost.GetById(id);
            if (existing is null) return null;
            existing.CostBase = costBase;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            _cost.Update(existing);
            return existing.Id;
        }
        var row = new CostSource(costBase, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            DataSource = DataSource.MANUAL
        };
        _cost.Add(row);
        return row.Id;
    }

    private long? UpsertRisk(long companyId, long? rowId, string classification, string name, string? note)
    {
        if (!Enum.TryParse<RiskScope>(classification, out var scope)) return null;
        if (rowId is { } id)
        {
            var existing = _risks.GetById(id);
            if (existing is null) return null;
            existing.Scope = scope;
            existing.Name = name;
            existing.Note = note;
            _risks.Update(existing);
            return existing.Id;
        }
        var row = new CompanyRisk(scope, name, companyId) { Note = note, DataSource = DataSource.MANUAL };
        _risks.Add(row);
        return row.Id;
    }

    // The company that owns a node's row (so References can scope the proof lookup without a row arg).
    private long? RowCompanyId(ExtractionNode node, long rowId) => node switch
    {
        ExtractionNode.COST => _cost.GetById(rowId)?.CompanyId,
        ExtractionNode.RISK => _risks.GetById(rowId)?.CompanyId,
        _                   => _revenue.GetById(rowId)?.CompanyId,
    };

    // Does this review belong to the given node's row?
    private static bool MatchesRow(SourceFieldReview r, ExtractionNode node, long rowId) => node switch
    {
        ExtractionNode.COST => r.CostSourceId == rowId,
        ExtractionNode.RISK => r.CompanyRiskId == rowId,
        _                   => r.RevenueSourceId == rowId,
    };

    // One current proof per (row, field) — the unique index demands upsert, not blind insert. New
    // proof clears any prior phase-2 verdict (stale-pass guard). The node decides which source FK
    // and RelationKind the review carries.
    private SourceFieldReview UpsertReviewByNode(
        ExtractionNode node, long companyId, long rowId, ReviewableField field, string endpoint,
        string pointer, string snapshot, string? referencedValue, long? filingId)
    {
        var review = _reviews.GetByCompany(companyId)
            .FirstOrDefault(r => MatchesRow(r, node, rowId) && r.Field == field);
        if (review is null)
        {
            review = new SourceFieldReview
            {
                CompanyId = companyId,
                Relation = node switch
                {
                    ExtractionNode.COST => RelationKind.COST,
                    ExtractionNode.RISK => RelationKind.RISK,
                    _                   => RelationKind.REVENUE,
                },
                RevenueSourceId = node == ExtractionNode.REVENUE ? rowId : null,
                CostSourceId    = node == ExtractionNode.COST ? rowId : null,
                CompanyRiskId   = node == ExtractionNode.RISK ? rowId : null,
                Field = field,
                Endpoint = endpoint,
                ReferencePointer = pointer,
                ReferenceSnapshot = snapshot,
                ReferencedValue = referencedValue,
                FilingId = filingId
            };
            _reviews.Add(review);
        }
        else
        {
            review.Endpoint = endpoint;
            review.ReferencePointer = pointer;
            review.ReferenceSnapshot = snapshot;
            review.ReferencedValue = referencedValue;
            review.FilingId = filingId;
            review.Mark = null;
            review.Rationale = null;
            review.ReviewedAt = null;
            review.ReviewerModel = null;
            _reviews.Update(review);
        }
        return review;
    }

    // Mode A — run the phase-2 AI reviewer over this company's unreviewed cells (human-entered
    // value + proof). Returns the pass/fail tally so the page can refresh its marks.
    [HttpPost, Route("review/{companyId:long}")]
    public async Task<IActionResult> Review(long companyId)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        try
        {
            var result = await _reviewer.ReviewCompanyAsync(companyId);
            return Json(result);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek unreachable.");
        }
    }

    // Mode B — AI reads one filing and proposes revenue rows + per-field proof for the human to
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
    public IActionResult ScanAutoAsync(
        long companyId, [FromQuery] string accession, [FromQuery] string doc,
        [FromQuery] string? node, [FromQuery] string? form,
        [FromQuery] string? companyName, [FromQuery] string? filingLabel)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");

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
        _jobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var extractor = scope.ServiceProvider.GetRequiredService<IFilingExtractionService>();
            var chat = scope.ServiceProvider.GetRequiredService<IExtractionChatService>();
            try
            {
                // Coarse phase reporting around the one awaited call — the widget polls Progress so
                // the user sees the worker is alive and what it's doing (no per-chunk hook exists).
                job.Progress = $"Reading the {job.FilingLabel} & triaging sections with parallel agents…";
                job.Report = await extractor.ScanAutoAsync(companyId, accession, doc, parsedNode, form);
                // Auto AI summary: one chat turn grounded on the digest the scan just cached, so the
                // notification opens with a real answer rather than just counts.
                job.Progress = $"Found {job.Report.Found} candidate(s) · writing summary…";
                var seed = new List<ChatMessage>
                {
                    new("user", "Summarize the candidates you found in this filing.")
                };
                var sb = new StringBuilder();
                await foreach (var d in chat.StreamReplyAsync(companyId, accession, doc, parsedNode, seed, form))
                    if (d.Kind == "text") sb.Append(d.Text);
                job.Summary = sb.ToString();
                job.Progress = "";
                job.Status = ScanJobStatus.Done;
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

    private static object ScanDto(ScanJob j) => new
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
        summary = j.Summary,
        error = j.Error
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
    public IActionResult ScanJobReply(string jobId, [FromBody] ScanJobReplyRequest req)
    {
        var job = _jobs.Get(jobId);
        if (job is null) return NotFound();
        if (job.Status != ScanJobStatus.Done) return BadRequest("Scan hasn't finished.");
        if (job.Replying) return Conflict("A reply is already in progress.");

        var node = ParseNode(job.Node);
        var history = req?.Messages ?? [];
        job.Replying = true;
        job.ReplyBuffer = "";
        job.ReplyThink = "";
        job.ReplyError = null;

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var chat = scope.ServiceProvider.GetRequiredService<IExtractionChatService>();
            try
            {
                await foreach (var d in chat.StreamReplyAsync(job.CompanyId, job.Accession, job.Doc, node, history, job.Form))
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

    // Mode B (conversational) — stream a chat grounded on the open filing. Emits NDJSON lines
    // {"t":"reasoning"|"text","c":"..."} as fragments arrive, so the page renders the thinking trace
    // and answer live. Persists nothing; ```save``` blocks in the answer pre-fill the form.
    [HttpPost, Route("chat")]
    public async Task Chat([FromBody] ChatRequest req, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        if (req is null || _companies.GetById(req.CompanyId) is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        try
        {
            await foreach (var d in _chat.StreamReplyAsync(req.CompanyId, req.Accession, req.Doc, ParseNode(req.Node), req.Messages, req.Form, ct))
            {
                await Response.WriteAsync(JsonSerializer.Serialize(new { t = d.Kind, c = d.Text }) + "\n", ct);
                await Response.Body.FlushAsync(ct);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            await Response.WriteAsync(JsonSerializer.Serialize(new { t = "error", c = "DeepSeek unreachable." }) + "\n", ct);
        }
    }

    // Web discovery — ask Perplexity sonar for the named counterparties behind the company's revenue
    // (Side=CUSTOMER) or cost (Side=SUPPLIER) segments. Runs Perplexity-style: a planner decomposes the
    // request into focused sub-queries, each its own grounded search. Streams NDJSON lines as it goes
    // ({"t":"plan"|"searching"|"result"|"error", …}) so the page renders a live feed. Persists nothing;
    // the user confirms each found counterparty via LinkCounterparty.
    [HttpPost, Route("discover-related")]
    public async Task DiscoverRelated([FromBody] DiscoverCounterpartiesRequest req, CancellationToken ct)
    {
        Response.ContentType = "application/x-ndjson; charset=utf-8";
        if (req is null || req.CompanyId <= 0 || _companies.GetById(req.CompanyId) is null)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

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

        var counterpartyId = req.ExistingCompanyId ?? await GetOrCreateCompanyAsync(req, owner);

        // CUSTOMER buys from us -> revenue source; SUPPLIER sells to us -> cost source.
        var node = string.Equals(req.Side, "SUPPLIER", StringComparison.OrdinalIgnoreCase)
            ? ExtractionNode.COST
            : ExtractionNode.REVENUE;
        var rowId = UpsertRowByNode(node, req.CompanyId, null, req.Classification, req.Name,
            value: req.Value, percentage: null, note: null, relatedCompanyId: counterpartyId);
        if (rowId is null) return BadRequest("Could not create the link (check the classification value).");

        // Save sonar's citation as proof on the new row's counterparty cell, so the web source the
        // relationship came from is recorded (and shown on the company's Details page).
        if (!string.IsNullOrWhiteSpace(req.SourceUrl))
            UpsertReviewByNode(node, req.CompanyId, rowId.Value, ReviewableField.RELATED_COMPANY,
                endpoint: "Perplexity sonar", pointer: req.SourceUrl,
                snapshot: string.IsNullOrWhiteSpace(req.Note) ? req.SourceUrl : req.Note,
                referencedValue: req.Name, filingId: null);

        // The relationship is symmetric but stored as two one-sided rows: owner gets a row pointing at
        // the counterparty (above); the counterparty needs the mirror row pointing back at owner, or its
        // Details page shows nothing. Owner's revenue (counterparty is its CUSTOMER) -> counterparty's
        // cost (owner is its supplier, COGS); owner's cost (counterparty is its supplier) ->
        // counterparty's revenue (owner is its CUSTOMER).
        EnsureReciprocal(node, counterpartyId, req.CompanyId, owner.Name, req.Value);

        return Json(new { sourceId = rowId.Value, counterpartyId, node = node.ToString() });
    }

    // Create the mirror source on the counterparty pointing back at owner, unless one already exists
    // (re-linking the same pair must not pile duplicates). Mirror node is the opposite of owner's:
    // REVENUE<->COST, with the natural default classification for that side (CUSTOMER / COGS).
    private void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName, double? value)
    {
        var (mirror, classification) = node == ExtractionNode.COST
            ? (ExtractionNode.REVENUE, nameof(SourceType.CUSTOMER))
            : (ExtractionNode.COST, nameof(CostBase.COGS));

        var exists = mirror == ExtractionNode.COST
            ? _cost.GetAll().Any(c => c.CompanyId == counterpartyId && c.RelatedCompanyId == ownerId)
            : _revenue.GetAll().Any(r => r.CompanyId == counterpartyId && r.RelatedCompanyId == ownerId);
        if (exists) return;

        UpsertRowByNode(mirror, counterpartyId, null, classification, ownerName,
            value: value, percentage: null, note: null, relatedCompanyId: ownerId);
    }

    // Reuse an existing company (fuzzy name match) or create one. When a ticker is known we run the
    // same FMP-by-ticker pipeline the New Company form uses (real profile: sector/industry/CIK/revenue);
    // otherwise fall back to a minimal company seeded from sonar's country/sector or the owner's.
    private async Task<long> GetOrCreateCompanyAsync(LinkCounterpartyRequest req, Company owner)
    {
        if (_companies.MatchByName(req.Name) is { } existing) return existing.Id;

        if (!string.IsNullOrWhiteSpace(req.Ticker))
        {
            FmpProfile? profile = null;
            try { profile = await _fmp.GetProfileAsync(req.Ticker.Trim()); }
            catch (HttpRequestException) { /* FMP down or symbol unknown -> stub below */ }

            if (profile is { CompanyName.Length: > 0 })
            {
                // sonar's name may differ from FMP's canonical one — re-check before inserting.
                if (_companies.MatchByName(profile.CompanyName) is { } byFmp) return byFmp.Id;

                FmpIncome? income = null;
                try { income = await _fmp.GetLatestIncomeAsync(req.Ticker.Trim()); }
                catch (HttpRequestException) { /* financials optional */ }

                var model = FmpMapper.ToCreateModel(profile, income);

                // FMP income is premium-gated (non-US -> 402), so it's often null. Mirror the New
                // Company form and backfill revenue + gross margin from Yahoo Finance.
                if (income == null)
                    await FillFinancialsFromYahoo(model, req.Ticker.Trim());
                var entity = new Company(model.Name, ResolveCountryId(profile.Country, owner), model.Sector)
                {
                    Cik = model.Cik,
                    Industry = model.Industry,
                    RevenueTotal = model.RevenueTotal,
                    GrossMargin = model.GrossMargin,
                    MarketCap = model.MarketCap,
                    AsOf = model.AsOf,
                    Notes = model.Notes
                };
                _companies.Add(entity);

                // Same dated financial history the New Company form pulls. Best-effort: a failure
                // (premium-gated, unreachable) must not block linking the counterparty.
                try { _companies.ReplaceFinancials(entity.Id, await _financials.BuildAsync(entity.Id, req.Ticker.Trim())); }
                catch (HttpRequestException) { /* financials unavailable — company still created */ }
                return entity.Id;
            }
        }

        // No ticker / FMP miss: minimal company so the link still works. Previously this left Industry
        // null; resolve it within the sector (the same classifier the New Company form uses) so
        // ticker-less companies aren't stuck unclassified. No ticker => treat as private.
        var sec = ParseSector(req.Sector) ?? owner.Sector;
        var stub = new Company(req.Name, ResolveCountryId(req.CountryCode, owner), sec)
        {
            Type = string.IsNullOrWhiteSpace(req.Ticker) ? CompanyType.PRIVATE : CompanyType.PUBLIC,
            Industry = await _industryClassifier.ClassifyAsync(sec, req.Name, null)
        };
        _companies.Add(stub);
        return stub.Id;
    }

    // Backfill revenue (USD) + gross margin from Yahoo Finance when FMP income is unavailable.
    // Margin is a currency-agnostic ratio so it's filled regardless; revenue is converted to USD
    // via ExchangeRate-API and left blank if no rate is available. Best-effort: Yahoo miss -> blanks.
    private async Task FillFinancialsFromYahoo(CompanyCreateModel model, string ticker)
    {
        var yf = await _yahoo.GetFinancialsAsync(ticker);
        if (yf == null) return;

        if (yf.GrossMargins is { } gm)
            model.GrossMargin = Math.Round(gm, 2); // 2 dp to match the form's step="0.01"

        if (yf.Revenue is { } rev && !string.IsNullOrWhiteSpace(yf.Currency))
        {
            var rate = await _exchangeRate.GetUsdRateAsync(yf.Currency);
            if (rate is { } r)
                model.RevenueTotal = Math.Round(rev * r);
        }
    }

    // Match an ISO-2 code against existing countries; fall back to the inspecting company's country
    // (always valid). Avoids creating a half-populated Country row for a counterparty.
    private long ResolveCountryId(string? iso2, Company owner) =>
        !string.IsNullOrWhiteSpace(iso2) &&
        _countries.GetAll().FirstOrDefault(c => string.Equals(c.Code, iso2, StringComparison.OrdinalIgnoreCase)) is { } m
            ? m.Id : owner.CountryId;

    // sonar returns GICS display form ("Information Technology"); the enum uses "INFORMATION_TECHNOLOGY".
    private static Sector? ParseSector(string? sector)
    {
        var normalized = sector?.Trim().ToUpperInvariant().Replace(' ', '_');
        return Enum.TryParse<Sector>(normalized, true, out var s) ? s : null;
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
