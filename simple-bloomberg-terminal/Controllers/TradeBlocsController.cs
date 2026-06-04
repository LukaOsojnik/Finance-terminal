using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("trade-blocs")]
public class TradeBlocsController : Controller
{
    private readonly ITradeBlocRepository _tradeBlocs;
    private readonly ICountryRepository _countries;

    public TradeBlocsController(ITradeBlocRepository tradeBlocs, ICountryRepository countries)
    {
        _tradeBlocs = tradeBlocs;
        _countries = countries;
    }

    [HttpGet, Route("")]
    public IActionResult Index() => View(_tradeBlocs.GetAll());

    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _tradeBlocs.Search(term));

    [HttpGet, Route("{id:long}/overview")]
    public IActionResult Details(long id)
    {
        var entity = _tradeBlocs.GetById(id);
        if (entity == null) return NotFound();
        return View(entity);
    }

    [HttpGet, Route("create", Name = "TradeBlocsCreate")]
    public IActionResult Create() { PopulateDropdowns(); return View(new TradeBlocCreateModel()); }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(TradeBlocCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new TradeBloc(model.Name, model.Code)
        {
            Description = model.Description,
            FoundedDate = model.FoundedDate
        };
        _tradeBlocs.Add(entity, model.SelectedCountryIds);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _tradeBlocs.GetById(id);
        if (entity == null) return NotFound();
        PopulateDropdowns();
        return View("Edit", ToEditModel(entity));
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _tradeBlocs.GetById(id);
        if (entity == null) return NotFound();
        var model = ToEditModel(entity);
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { PopulateDropdowns(); return View("Edit", model); }
        entity.Name = model.Name;
        entity.Code = model.Code;
        entity.Description = model.Description;
        entity.FoundedDate = model.FoundedDate;
        _tradeBlocs.Update(entity, model.SelectedCountryIds);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete", Name = "TradeBlocDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _tradeBlocs.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    private void PopulateDropdowns() => ViewBag.Countries = _countries.GetAll().ToList();

    private static TradeBlocEditModel ToEditModel(TradeBloc t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        Code = t.Code,
        Description = t.Description,
        FoundedDate = t.FoundedDate,
        SelectedCountryIds = t.Countries.Select(c => c.Id).ToList()
    };
}
