using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("revenue-sources")]
[Authorize(Roles = "Admin,Manager")]
public class RevenueSourcesController : Controller
{
    private readonly IRevenueSourceRepository _repo;
    private readonly ICompanyRepository _companies;
    private readonly IFilingRepository _filings;

    public RevenueSourcesController(
        IRevenueSourceRepository repo,
        ICompanyRepository companies,
        IFilingRepository filings)
    {
        _repo = repo;
        _companies = companies;
        _filings = filings;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() => View(_repo.GetAll());

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _repo.Search(term));

    [AllowAnonymous]
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
            CompanyFilings = _filings.GetByCompany(entity.CompanyId).ToList()
        };
        return View(vm);
    }

    // Clear the row's proof: drop its reference, evidence and filing link in one go.
    [HttpPost, Route("{id:long}/proof/detach"), ValidateAntiForgeryToken]
    public IActionResult DetachProof(long id)
    {
        if (_repo.GetById(id) is { } row)
        {
            row.Reference = null;
            row.Evidence = null;
            row.FilingId = null;
            _repo.Update(row);
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // Replace which filing backs this row. Identified by accession so a filing not yet in the DB
    // (just browsed from EDGAR) gets upserted and attached. Blank accession clears the link.
    [HttpPost, Route("{id:long}/proof/filing"), ValidateAntiForgeryToken]
    public IActionResult SetProofFiling(long id,
        string? filingAccession, string? filingForm, string? filingDate, string? filingUrl)
    {
        var row = _repo.GetById(id);
        if (row is null) return RedirectToAction(nameof(Details), new { id });

        if (string.IsNullOrWhiteSpace(filingAccession))
        {
            row.FilingId = null;
        }
        else
        {
            DateTime? d = DateTime.TryParse(filingDate, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dd)
                ? dd : null;
            row.FilingId = _filings.Upsert(row.CompanyId, filingAccession, filingForm, d, filingUrl).Id;
        }
        _repo.Update(row);
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

    // Cascade: removes this source + its source filing + every other source on that filing.
    // returnUrl lets the company profile send the user back to itself after deleting.
    [HttpPost, Route("{id:long}/delete", Name = "RevenueSourceDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id, string? returnUrl)
    {
        _filings.SoftDeleteSourceCluster(ExtractionNode.REVENUE, id);
        if (Url.IsLocalUrl(returnUrl)) return Redirect(returnUrl);
        return RedirectToAction(nameof(Index));
    }

    private void PopulateDropdowns()
    {
        ViewBag.SourceTypes = EnumSelect.Of<SourceType>();
        ViewBag.DataSources = EnumSelect.Of<DataSource>();
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
