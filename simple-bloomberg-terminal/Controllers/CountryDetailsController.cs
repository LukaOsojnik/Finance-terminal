using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("country-details")]
[Authorize(Roles = "Admin,Manager")]
public class CountryDetailsController : Controller
{
    private readonly ICountryDetailsRepository _details;
    private readonly ICountryRepository _countries;

    public CountryDetailsController(ICountryDetailsRepository details, ICountryRepository countries)
    {
        _details = details;
        _countries = countries;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() => View(_details.GetAll());

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _details.Search(term));

    [HttpGet, Route("validate-country")]
    public IActionResult ValidateCountry(long countryId) =>
        Json(_details.GetByCountryId(countryId) == null);

    [HttpGet, Route("create", Name = "CountryDetailsCreate")]
    public IActionResult Create() => View(new CountryDetailsCreateModel());

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CountryDetailsCreateModel model)
    {
        if (_details.GetByCountryId(model.CountryId) != null)
            ModelState.AddModelError(nameof(model.CountryId), "Country already has details (1:1).");
        if (!ModelState.IsValid) return View(model);
        _details.Add(new CountryDetails { CountryId = model.CountryId, MarketPosition = model.MarketPosition });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{countryId:long}/edit")]
    public IActionResult EditGet(long countryId)
    {
        var entity = _details.GetByCountryId(countryId);
        if (entity == null) return NotFound();
        return View("Edit", new CountryDetailsEditModel { CountryId = entity.CountryId, MarketPosition = entity.MarketPosition });
    }

    [HttpPost, ActionName("Edit"), Route("{countryId:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long countryId)
    {
        var entity = _details.GetByCountryId(countryId);
        if (entity == null) return NotFound();
        var model = new CountryDetailsEditModel { CountryId = entity.CountryId, MarketPosition = entity.MarketPosition };
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) return View("Edit", model);
        entity.MarketPosition = model.MarketPosition;
        _details.Update(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{countryId:long}/delete", Name = "CountryDetailsDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long countryId)
    {
        try { _details.SoftDelete(countryId); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }
}
