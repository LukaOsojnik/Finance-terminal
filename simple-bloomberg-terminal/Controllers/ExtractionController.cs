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
    private readonly ISourceFieldReviewRepository _reviews;
    private readonly ICompanyRepository _companies;
    private readonly IFilingRepository _filings;
    private readonly IReviewService _reviewer;
    private readonly IFilingExtractionService _extractor;
    private readonly IExtractionChatService _chat;

    public ExtractionController(
        IRevenueSourceRepository revenue,
        ISourceFieldReviewRepository reviews,
        ICompanyRepository companies,
        IFilingRepository filings,
        IReviewService reviewer,
        IFilingExtractionService extractor,
        IExtractionChatService chat)
    {
        _revenue = revenue;
        _reviews = reviews;
        _companies = companies;
        _filings = filings;
        _reviewer = reviewer;
        _extractor = extractor;
        _chat = chat;
    }

    [HttpGet, Route("")]
    public IActionResult Index(long? companyId, long? revenueSourceId)
    {
        var vm = new ExtractionIndexViewModel { CompanyId = companyId };

        // Opened from a source's Details "Add references": prefill that row's values so the user
        // browses EDGAR against the existing source instead of a blank new row.
        if (revenueSourceId is { } rowId && _revenue.GetById(rowId) is { } row)
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

        ViewBag.SourceTypes = Enum.GetValues<SourceType>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
        return View(vm);
    }

    // Existing references for a source row, so the page can show each cell's pointer on load.
    [HttpGet, Route("references/{revenueSourceId:long}")]
    public IActionResult References(long revenueSourceId)
    {
        var row = _revenue.GetById(revenueSourceId);
        if (row is null) return NotFound();
        var refs = _reviews.GetByCompany(row.CompanyId)
            .Where(r => r.RevenueSourceId == revenueSourceId)
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
        if (!Enum.TryParse<SourceType>(req.SourceType, out var sourceType)) return BadRequest("Invalid source type.");

        // Proof filing: upsert by accession when the proof came from a filing document; null from
        // Company Facts. Attached per-field, so one source can cite different filings per cell.
        var filingId = ResolveFilingId(req.CompanyId, req.FilingAccessionNumber, req.FilingForm, req.FilingDate, req.FilingUrl);

        // The source row is the source of truth for the values — upsert it first so a review FK can
        // only ever point at a row that exists.
        var row = UpsertRow(req.CompanyId, req.RevenueSourceId, sourceType, req.Name, req.Value, req.Percentage, req.RelatedCompanyId);
        if (row is null) return NotFound("Source row not found.");

        var endpoint = string.IsNullOrWhiteSpace(req.Endpoint)
            ? $"POST /api/stock/refresh/{req.CompanyId}"
            : req.Endpoint;
        var review = UpsertReview(req.CompanyId, row.Id, field, endpoint,
            req.ReferencePointer, req.ReferenceSnapshot, req.ReferencedValue, filingId);
        return Json(new ReferenceResult(row.Id, review.Id, field.ToString()));
    }

    // One button to save the whole form: upsert the row, then upsert a proof per field that carries
    // one. Replaces the old per-cell "Use as reference" flow.
    [HttpPost, Route("save")]
    public IActionResult Save([FromBody] SaveRequest req)
    {
        if (req is null || req.CompanyId <= 0) return BadRequest("CompanyId required.");
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest("Name required.");
        if (!Enum.TryParse<SourceType>(req.SourceType, out var sourceType)) return BadRequest("Invalid source type.");

        var row = UpsertRow(req.CompanyId, req.RevenueSourceId, sourceType, req.Name, req.Value, req.Percentage, req.RelatedCompanyId);
        if (row is null) return NotFound("Source row not found.");

        var saved = 0;
        foreach (var p in req.Proofs ?? [])
        {
            if (string.IsNullOrWhiteSpace(p.ReferenceSnapshot)) continue;
            if (!Enum.TryParse<ReviewableField>(p.Field, out var field)) continue;
            var filingId = ResolveFilingId(req.CompanyId, p.FilingAccessionNumber, p.FilingForm, p.FilingDate, p.FilingUrl);
            var endpoint = string.IsNullOrWhiteSpace(p.Endpoint) ? "AI extraction" : p.Endpoint;
            UpsertReview(req.CompanyId, row.Id, field, endpoint, p.ReferencePointer, p.ReferenceSnapshot, p.ReferencedValue, filingId);
            saved++;
        }
        return Json(new { revenueSourceId = row.Id, proofs = saved });
    }

    // Create or update the source row from the form values. Returns null only when an existing-row
    // id was given but no such row exists (caller maps that to NotFound).
    private RevenueSource? UpsertRow(
        long companyId, long? rowId, SourceType sourceType, string name,
        double? value, double? percentage, long? relatedCompanyId)
    {
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
            return existing;
        }

        var row = new RevenueSource(sourceType, name, companyId)
        {
            Value = value,
            Percentage = percentage,
            RelatedCompanyId = relatedCompanyId,
            DataSource = DataSource.MANUAL
        };
        _revenue.Add(row);   // assigns row.Id
        return row;
    }

    // One current proof per (row, field) — the unique index demands upsert, not blind insert. New
    // proof clears any prior phase-2 verdict (stale-pass guard).
    private SourceFieldReview UpsertReview(
        long companyId, long rowId, ReviewableField field, string endpoint,
        string pointer, string snapshot, string? referencedValue, long? filingId)
    {
        var review = _reviews.GetByCompany(companyId)
            .FirstOrDefault(r => r.RevenueSourceId == rowId && r.Field == field);
        if (review is null)
        {
            review = new SourceFieldReview
            {
                CompanyId = companyId,
                Relation = RelationKind.REVENUE,
                RevenueSourceId = rowId,
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
    public async Task<IActionResult> AutoExtract(long companyId, [FromQuery] string accession, [FromQuery] string doc)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            var suggestions = await _extractor.ExtractAsync(companyId, accession, doc);
            return Json(suggestions);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, "DeepSeek unreachable.");
        }
    }

    // Mode B (curated) — bold sub-headings inside Items 7/8/1A for the user to pick from.
    [HttpGet, Route("headings/{companyId:long}")]
    public async Task<IActionResult> Headings(long companyId, [FromQuery] string accession, [FromQuery] string doc)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        try
        {
            return Json(await _extractor.GetHeadingsAsync(companyId, accession, doc));
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
        long companyId, [FromQuery] string accession, [FromQuery] string doc, [FromBody] int[] headingIds)
    {
        if (_companies.GetById(companyId) is null) return NotFound();
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc))
            return BadRequest("accession and doc are required.");
        if (headingIds is null || headingIds.Length == 0) return BadRequest("Select at least one section.");
        try
        {
            var found = await _extractor.ScanSelectedHeadingsAsync(companyId, accession, doc, headingIds);
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
            await foreach (var d in _chat.StreamReplyAsync(req.CompanyId, req.Accession, req.Doc, req.Messages, ct))
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
