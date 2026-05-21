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

    public RevenueSourcesController(IRevenueSourceRepository repo, ICompanyRepository companies)
    {
        _repo = repo;
        _companies = companies;
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
        return View(entity);
    }

    [HttpGet, Route("create")]
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
    public async Task<IActionResult> EditPost(long id)
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
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _repo.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
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
