using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("revenue-sources")]
public class RevenueSourcesController : Controller
{
    private readonly IRevenueSourceRepository _repo;
    private readonly ICompanyRepository _companies;
    private readonly IFilingRepository _filings;
    private readonly ISourceFieldReviewRepository _reviews;

    public RevenueSourcesController(
        IRevenueSourceRepository repo,
        ICompanyRepository companies,
        IFilingRepository filings,
        ISourceFieldReviewRepository reviews)
    {
        _repo = repo;
        _companies = companies;
        _filings = filings;
        _reviews = reviews;
    }

    [HttpGet, Route("")]
    public IActionResult Index() => View(_repo.GetAll());

    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _repo.Search(term));

    [HttpGet, Route("{id:long}/breakdown")]
    public IActionResult Details(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();

        PopulateDropdowns();
        ViewBag.CompanyLabel = entity.Company?.Name;
        ViewBag.RelatedCompanyLabel = entity.RelatedCompany?.Name;

        var vm = new RevenueSourceDetailViewModel
        {
            Source = entity,
            Edit = ToEditModel(entity),
            Reviews = _reviews.GetByCompany(entity.CompanyId)
                .Where(r => r.RevenueSourceId == id)
                .OrderBy(r => r.Field)
                .ToList(),
            CompanyFilings = _filings.GetByCompany(entity.CompanyId).ToList()
        };
        return View(vm);
    }

    // Detach one field's proof: soft-delete that review (removes its filing link too).
    [HttpPost, Route("{id:long}/reviews/{reviewId:long}/detach"), ValidateAntiForgeryToken]
    public IActionResult DetachReview(long id, long reviewId)
    {
        var review = _reviews.GetById(reviewId);
        if (review is not null && review.RevenueSourceId == id)
            _reviews.SoftDelete(reviewId);
        return RedirectToAction(nameof(Details), new { id });
    }

    // Replace which filing backs one field. Identified by accession so a filing not yet in the DB
    // (just browsed from EDGAR) gets upserted and attached. Blank accession clears the link. New
    // proof context, so the phase-2 verdict is reset.
    [HttpPost, Route("{id:long}/reviews/{reviewId:long}/filing"), ValidateAntiForgeryToken]
    public IActionResult SetReviewFiling(long id, long reviewId,
        string? filingAccession, string? filingForm, string? filingDate, string? filingUrl)
    {
        var review = _reviews.GetById(reviewId);
        if (review is null || review.RevenueSourceId != id) return RedirectToAction(nameof(Details), new { id });

        if (string.IsNullOrWhiteSpace(filingAccession))
        {
            review.FilingId = null;
        }
        else
        {
            DateTime? d = DateTime.TryParse(filingDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)
                ? dd : null;
            review.FilingId = _filings.Upsert(review.CompanyId, filingAccession, filingForm, d, filingUrl).Id;
        }

        review.Mark = null;
        review.Rationale = null;
        review.ReviewedAt = null;
        review.ReviewerModel = null;
        _reviews.Update(review);
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet, Route("create", Name = "RevenueSourcesCreate")]
    public IActionResult Create() { PopulateDropdowns(); return View(new RevenueSourceCreateModel()); }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(RevenueSourceCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new RevenueSource(model.SourceType, model.Name, model.CompanyId)
        {
            Value = model.Value,
            Percentage = model.Percentage,
            DataSource = model.DataSource,
            RelatedCompanyId = model.RelatedCompanyId
        };
        _repo.Add(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        PopulateDropdowns();
        ViewBag.CompanyLabel = entity.Company?.Name;
        ViewBag.RelatedCompanyLabel = entity.RelatedCompany?.Name;
        return View("Edit", ToEditModel(entity));
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id, string? returnUrl)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        var model = ToEditModel(entity);
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { PopulateDropdowns(); ViewBag.CompanyLabel = entity.Company?.Name; ViewBag.RelatedCompanyLabel = entity.RelatedCompany?.Name; return View("Edit", model); }
        entity.SourceType = model.SourceType;
        entity.Name = model.Name;
        entity.Value = model.Value;
        entity.Percentage = model.Percentage;
        entity.DataSource = model.DataSource;
        entity.CompanyId = model.CompanyId;
        entity.RelatedCompanyId = model.RelatedCompanyId;
        _repo.Update(entity);
        if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    // Cascade: removes this source + its proof reviews + its filing + every other source on that
    // filing. returnUrl lets the company profile send the user back to itself after deleting.
    [HttpPost, Route("{id:long}/delete", Name = "RevenueSourceDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id, string? returnUrl)
    {
        _filings.SoftDeleteSourceCluster(RelationKind.REVENUE, id);
        if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    private void PopulateDropdowns()
    {
        ViewBag.SourceTypes = Enum.GetValues<SourceType>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
        ViewBag.DataSources = Enum.GetValues<DataSource>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
    }

    private static RevenueSourceEditModel ToEditModel(RevenueSource r) => new()
    {
        Id = r.Id,
        SourceType = r.SourceType,
        Name = r.Name,
        Value = r.Value,
        Percentage = r.Percentage,
        DataSource = r.DataSource,
        CompanyId = r.CompanyId,
        RelatedCompanyId = r.RelatedCompanyId
    };
}
