using System.Globalization;
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
    private readonly IFilingRepository _filings;
    private readonly IReviewService _reviewer;
    private readonly IFilingExtractionService _extractor;
    private readonly IExtractionChatService _chat;

    public ExtractionController(
        IRevenueSourceRepository revenue,
        ICostSourceRepository cost,
        ICompanyRiskRepository risks,
        ISourceFieldReviewRepository reviews,
        ICompanyRepository companies,
        IFilingRepository filings,
        IReviewService reviewer,
        IFilingExtractionService extractor,
        IExtractionChatService chat)
    {
        _revenue = revenue;
        _cost = cost;
        _risks = risks;
        _reviews = reviews;
        _companies = companies;
        _filings = filings;
        _reviewer = reviewer;
        _extractor = extractor;
        _chat = chat;
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
    public async Task<IActionResult> AutoExtract(long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromQuery] string? node)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            var suggestions = await _extractor.ExtractAsync(companyId, accession, doc, ParseNode(node));
            return Json(suggestions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek unreachable.");
        }
    }

    // Mode B (curated) — bold sub-headings inside Items 7/8/1A for the user to pick from.
    [HttpGet, Route("headings/{companyId:long}")]
    public async Task<IActionResult> Headings(long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromQuery] string? node)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            return Json(await _extractor.GetHeadingsAsync(companyId, accession, doc, ParseNode(node)));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "SEC unreachable.");
        }
    }

    // Mode B (curated) — spawn one worker per picked heading, scan only those paragraphs, and stash
    // the result as the chat's grounding. Returns the candidate count.
    [HttpPost, Route("scan-headings/{companyId:long}")]
    public async Task<IActionResult> ScanHeadings(
        long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromQuery] string? node, [FromBody] int[] headingIds)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        if (headingIds is null || headingIds.Length == 0) return BadRequest("Select at least one section.");
        try
        {
            var found = await _extractor.ScanSelectedHeadingsAsync(companyId, accession, doc, ParseNode(node), headingIds);
            return Json(new { findings = found });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek/SEC unreachable.");
        }
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
            await foreach (var d in _chat.StreamReplyAsync(req.CompanyId, req.Accession, req.Doc, ParseNode(req.Node), req.Messages, ct))
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
