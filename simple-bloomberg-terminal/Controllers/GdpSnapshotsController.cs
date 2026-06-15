using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("gdp-snapshots")]
[Authorize(Roles = "Admin,Manager")]
public class GdpSnapshotsController : Controller
{
    private readonly IGdpSnapshotRepository _repo;
    private readonly ICountryRepository _countries;

    public GdpSnapshotsController(IGdpSnapshotRepository repo, ICountryRepository countries)
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

    [HttpGet, Route("create", Name = "GdpSnapshotsCreate")]
    public IActionResult Create() => View(new GdpSnapshotCreateModel());

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(GdpSnapshotCreateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        _repo.Add(new GdpSnapshot { CountryId = model.CountryId, Year = model.Year, GdpUsd = model.GdpUsd });
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        ViewBag.CountryLabel = entity.Country?.Name;
        return View("Edit", new GdpSnapshotEditModel { Id = entity.Id, CountryId = entity.CountryId, Year = entity.Year, GdpUsd = entity.GdpUsd });
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return NotFound();
        var model = new GdpSnapshotEditModel { Id = entity.Id, CountryId = entity.CountryId, Year = entity.Year, GdpUsd = entity.GdpUsd };
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { ViewBag.CountryLabel = entity.Country?.Name; return View("Edit", model); }
        entity.CountryId = model.CountryId;
        entity.Year = model.Year;
        entity.GdpUsd = model.GdpUsd;
        _repo.Update(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete", Name = "GdpSnapshotDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id) =>
        this.SoftDeleteRedirect(() => _repo.SoftDelete(id));
}
