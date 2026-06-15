using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("country-advantages")]
[Authorize(Roles = "Admin,Manager")]
public class CountryAdvantagesController : Controller
{
    private readonly ICountryAdvantageRepository _repo;
    private readonly ICountryRepository _countries;

    public CountryAdvantagesController(ICountryAdvantageRepository repo, ICountryRepository countries)
    {
        _repo = repo;
        _countries = countries;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() => View(_repo.GetAll());

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) => PartialView("_TableBody", _repo.Search(term));

    [HttpGet, Route("create", Name = "CountryAdvantagesCreate")]
    public IActionResult Create() => View(new CountryAdvantageCreateModel());

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CountryAdvantageCreateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        _repo.Add(new CountryAdvantage { CountryId = model.CountryId, Text = model.Text });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        ViewBag.CountryLabel = entity.Country?.Name;
        return View("Edit", new CountryAdvantageEditModel { Id = entity.Id, CountryId = entity.CountryId, Text = entity.Text });
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        var model = new CountryAdvantageEditModel { Id = entity.Id, CountryId = entity.CountryId, Text = entity.Text };
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { ViewBag.CountryLabel = entity.Country?.Name; return View("Edit", model); }
        entity.CountryId = model.CountryId;
        entity.Text = model.Text;
        _repo.Update(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete", Name = "CountryAdvantageDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id) =>
        this.SoftDeleteRedirect(() => _repo.SoftDelete(id));
}
