using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("countries")]
public class CountriesController : Controller
{
    private readonly ICountryRepository _countries;
    private readonly ICountryDetailsRepository _countryDetails;
    private readonly ICompanyRepository _companies;

    public CountriesController(
        ICountryRepository countries,
        ICountryDetailsRepository countryDetails,
        ICompanyRepository companies)
    {
        _countries = countries;
        _countryDetails = countryDetails;
        _companies = companies;
    }

    [HttpGet, Route("")]
    public IActionResult Index() =>
        View(_countries.GetAll().Select(CountryRowViewModel.From));

    [HttpGet, Route("search")]
    public IActionResult Search(string? term)
    {
        var rows = _countries.Search(term).Select(CountryRowViewModel.From);
        return PartialView("_TableBody", rows);
    }

    [HttpGet, Route("lookup")]
    public IActionResult Lookup(string? term) =>
        Json(_countries.Search(term).Take(10).Select(c => new { id = c.Id, label = c.Name }));

    [HttpGet, Route("{id:long}/overview")]
    public IActionResult Details(long id)
    {
        var country = _countries.GetById(id);
        if (country is null) return NotFound();

        var details = _countryDetails.GetByCountryId(id);
        var topCompanies = _companies.GetAll().Where(c => c.CountryId == id).ToList();

        var viewModel = new CountryDetailsViewModel
        {
            Country = country,
            MarketPosition = details?.MarketPosition,
            Advantages = details?.Advantages ?? [],
            Challenges = details?.Challenges ?? [],
            TopCompanies = topCompanies,
            TradeBlocs = country.TradeBlocs.ToList(),
            GdpHistory = details?.GdpHistory ?? [],
        };
        return View(viewModel);
    }

    [HttpGet, Route("create")]
    public IActionResult Create() => View(new CountryCreateModel());

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(CountryCreateModel model)
    {
        if (!ModelState.IsValid) return View(model);
        var entity = new Country(model.Code, model.Name, model.Region, model.CurrencyCode)
        {
            GdpUsd = model.GdpUsd,
            Population = model.Population,
            RiskRating = model.RiskRating,
            Notes = model.Notes
        };
        _countries.Add(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _countries.GetById(id);
        if (entity == null) return NotFound();
        return View("Edit", ToEditModel(entity));
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _countries.GetById(id);
        if (entity == null) return NotFound();

        var model = ToEditModel(entity);
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) return View("Edit", model);

        ApplyEdit(entity, model);
        _countries.Update(entity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _countries.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    private static CountryEditModel ToEditModel(Country e) => new()
    {
        Id = e.Id,
        Code = e.Code,
        Name = e.Name,
        Region = e.Region,
        CurrencyCode = e.CurrencyCode,
        GdpUsd = e.GdpUsd,
        Population = e.Population,
        RiskRating = e.RiskRating,
        Notes = e.Notes
    };

    private static void ApplyEdit(Country e, CountryEditModel m)
    {
        e.Code = m.Code;
        e.Name = m.Name;
        e.Region = m.Region;
        e.CurrencyCode = m.CurrencyCode;
        e.GdpUsd = m.GdpUsd;
        e.Population = m.Population;
        e.RiskRating = m.RiskRating;
        e.Notes = m.Notes;
    }
}
