using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("companies")]
public class CompaniesController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;

    public CompaniesController(ICompanyRepository companies, ICountryRepository countries)
    {
        _companies = companies;
        _countries = countries;
    }

    [HttpGet, Route("")]
    public IActionResult Index() => View(_companies.GetAll());

    [HttpGet, Route("search")]
    public IActionResult Search(string? term) =>
        PartialView("_TableBody", _companies.Search(term));

    [HttpGet, Route("lookup")]
    public IActionResult Lookup(string? term) =>
        Json(_companies.Lookup(term).Take(10).Select(c => new { id = c.Id, label = c.Name }));

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
            RevenueSources = company.RevenueSources.Where(r => r.DeletedAt == null),
            CostSources = company.CostSources.Where(c => c.DeletedAt == null),
            SectorLabel = company.Sector.ToString().Replace("_", " "),
            IndustryLabel = company.Industry.HasValue
                ? company.Industry.Value.ToString().Replace("_", " ")
                : "—"
        };
        return View(vm);
    }

    [HttpGet, Route("create")]
    public IActionResult Create()
    {
        PopulateDropdowns();
        return View(new CompanyCreateModel());
    }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CompanyCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new Company(model.Name, model.CountryId, model.Sector)
        {
            Cik = model.Cik,
            Industry = model.Industry,
            RevenueTotal = model.RevenueTotal,
            GrossMargin = model.GrossMargin,
            AsOf = model.AsOf,
            Notes = model.Notes
        };
        _companies.Add(entity);
        return RedirectToAction(nameof(Index));
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

    [HttpPost, Route("{id:long}/delete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _companies.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
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
        CountryId = c.CountryId,
        Sector = c.Sector,
        Industry = c.Industry,
        RevenueTotal = c.RevenueTotal,
        GrossMargin = c.GrossMargin,
        AsOf = c.AsOf,
        Notes = c.Notes
    };

    private static void ApplyEdit(Company c, CompanyEditModel m)
    {
        c.Name = m.Name;
        c.Cik = m.Cik;
        c.CountryId = m.CountryId;
        c.Sector = m.Sector;
        c.Industry = m.Industry;
        c.RevenueTotal = m.RevenueTotal;
        c.GrossMargin = m.GrossMargin;
        c.AsOf = m.AsOf;
        c.Notes = m.Notes;
    }
}
