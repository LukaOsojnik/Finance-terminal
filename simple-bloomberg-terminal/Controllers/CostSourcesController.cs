using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("cost-sources")]
public class CostSourcesController : Controller
{
    private readonly ICostSourceRepository _repo;
    private readonly ICompanyRepository _companies;

    public CostSourcesController(ICostSourceRepository repo, ICompanyRepository companies)
    {
        _repo = repo;
        _companies = companies;
    }

    [HttpGet, Route("")]
    public IActionResult Index() => View(_repo.GetAll());

    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _repo.Search(term));

    [HttpGet, Route("create")]
    public IActionResult Create() { PopulateDropdowns(); return View(new CostSourceCreateModel()); }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CostSourceCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new CostSource(model.CostBase, model.Name, model.CompanyId)
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
        entity.CostBase = model.CostBase;
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
        ViewBag.CostBases = Enum.GetValues<CostBase>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
        ViewBag.DataSources = Enum.GetValues<DataSource>()
            .Select(t => new SelectListItem(t.ToString(), t.ToString())).ToList();
    }

    private static CostSourceEditModel ToEditModel(CostSource c) => new()
    {
        Id = c.Id,
        CostBase = c.CostBase,
        Name = c.Name,
        Value = c.Value,
        Percentage = c.Percentage,
        DataSource = c.DataSource,
        CompanyId = c.CompanyId,
        RelatedCompanyId = c.RelatedCompanyId
    };
}
