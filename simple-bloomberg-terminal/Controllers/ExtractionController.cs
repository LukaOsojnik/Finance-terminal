using System.Globalization;
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

    public ExtractionController(
        IRevenueSourceRepository revenue,
        ISourceFieldReviewRepository reviews,
        ICompanyRepository companies,
        IFilingRepository filings)
    {
        _revenue = revenue;
        _reviews = reviews;
        _companies = companies;
        _filings = filings;
    }

    [HttpGet, Route("")]
    public IActionResult Index(long? companyId)
    {
        var vm = new ExtractionIndexViewModel { CompanyId = companyId };
        if (companyId is { } id)
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

        // Resolve the proof filing (upsert by accession) when the proof came from a filing
        // document. null when the proof was taken from Company Facts. The filing is attached to
        // this cell's review (below), so one source can cite different filings per field.
        var filingId = ResolveFilingId(req);

        // 1. The source row is the source of truth for the values — create or update it first,
        //    so a review FK can only ever point at a row that exists.
        RevenueSource row;
        if (req.RevenueSourceId is { } id)
        {
            var existing = _revenue.GetById(id);
            if (existing is null) return NotFound("Source row not found.");
            existing.SourceType = sourceType;
            existing.Name = req.Name;
            existing.Value = req.Value;
            existing.Percentage = req.Percentage;
            existing.RelatedCompanyId = req.RelatedCompanyId;
            _revenue.Update(existing);
            row = existing;
        }
        else
        {
            row = new RevenueSource(sourceType, req.Name, req.CompanyId)
            {
                Value = req.Value,
                Percentage = req.Percentage,
                RelatedCompanyId = req.RelatedCompanyId,
                DataSource = DataSource.MANUAL
            };
            _revenue.Add(row);   // assigns row.Id
        }

        var endpoint = string.IsNullOrWhiteSpace(req.Endpoint)
            ? $"POST /api/stock/refresh/{req.CompanyId}"
            : req.Endpoint;

        // 2. One current reference per (row, field) — unique index demands upsert, not blind insert.
        var review = _reviews.GetByCompany(req.CompanyId)
            .FirstOrDefault(r => r.RevenueSourceId == row.Id && r.Field == field);
        if (review is null)
        {
            review = new SourceFieldReview
            {
                CompanyId = req.CompanyId,
                Relation = RelationKind.REVENUE,
                RevenueSourceId = row.Id,
                Field = field,
                Endpoint = endpoint,
                ReferencePointer = req.ReferencePointer,
                ReferenceSnapshot = req.ReferenceSnapshot,
                ReferencedValue = req.ReferencedValue,
                FilingId = filingId
            };
            _reviews.Add(review);
        }
        else
        {
            // New proof invalidates any prior phase-2 verdict (stale-pass guard).
            review.Endpoint = endpoint;
            review.ReferencePointer = req.ReferencePointer;
            review.ReferenceSnapshot = req.ReferenceSnapshot;
            review.ReferencedValue = req.ReferencedValue;
            review.FilingId = filingId;   // reflects the current proof's filing (null from Company Facts)
            review.Mark = null;
            review.Rationale = null;
            review.ReviewedAt = null;
            review.ReviewerModel = null;
            _reviews.Update(review);
        }

        return Json(new ReferenceResult(row.Id, review.Id, field.ToString()));
    }

    // Upsert the proof filing by accession (globally unique) and return its id. Returns null when
    // no filing was open (proof came from Company Facts) so the caller leaves the link untouched.
    private long? ResolveFilingId(ReferenceRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.FilingAccessionNumber)) return null;

        DateTime? filingDate = DateTime.TryParse(
            req.FilingDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d : null;

        // Upsert by accession (revives a soft-deleted row instead of a duplicate-insert).
        return _filings.Upsert(req.CompanyId, req.FilingAccessionNumber, req.FilingForm, filingDate, req.FilingUrl).Id;
    }
}
