using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

[Route("companies")]
public class CompaniesController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;
    private readonly IFmpApiClient _fmp;
    private readonly IRestCountriesClient _restCountries;
    private readonly IYahooFinanceClient _yahoo;
    private readonly IExchangeRateApiClient _exchangeRate;

    public CompaniesController(ICompanyRepository companies, ICountryRepository countries,
        IFmpApiClient fmp, IRestCountriesClient restCountries,
        IYahooFinanceClient yahoo, IExchangeRateApiClient exchangeRate)
    {
        _companies = companies;
        _countries = countries;
        _fmp = fmp;
        _restCountries = restCountries;
        _yahoo = yahoo;
        _exchangeRate = exchangeRate;
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

    // Named route: both this MVC controller and the API CompaniesController have a "Create"
    // action, so a bare asp-action="Create" link is ambiguous and resolves to /api/Companies.
    // The name lets the view target this GET form unambiguously.
    [HttpGet, Route("create", Name = "CompaniesCreate")]
    public IActionResult Create()
    {
        PopulateDropdowns();
        return View(new CompanyCreateModel());
    }

    // Prefill the create form from FMP by ticker. Does not save — returns the Create view with
    // the mapped model so the user reviews/edits before submitting the normal Create POST.
    [HttpPost, Route("fetch"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Fetch(string? symbol)
    {
        PopulateDropdowns();

        if (string.IsNullOrWhiteSpace(symbol))
        {
            ModelState.AddModelError("", "Enter a ticker symbol.");
            return View("Create", new CompanyCreateModel());
        }

        var ticker = symbol.Trim();
        FmpProfile? profile;
        try
        {
            profile = await _fmp.GetProfileAsync(ticker);
        }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "FMP is unreachable. Try again or enter the company manually.");
            return View("Create", new CompanyCreateModel());
        }

        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName))
        {
            ModelState.AddModelError("", $"No company found for ticker '{ticker}'.");
            return View("Create", new CompanyCreateModel());
        }

        // Financials are a premium endpoint for some symbols (e.g. non-US return HTTP 402), so
        // treat income as optional — keep the profile-driven fields and leave financials blank.
        FmpIncome? income = null;
        try { income = await _fmp.GetLatestIncomeAsync(ticker); }
        catch (HttpRequestException) { /* income unavailable on this plan/symbol */ }

        var model = FmpMapper.ToCreateModel(profile, income);

        var country = await ResolveOrCreateCountry(profile.Country);
        if (country != null)
        {
            model.CountryId = country.Id;
            ViewBag.CountryLabel = country.Name;
        }

        if (income == null)
            await ApplyYahooFallback(model, ticker);
        else if (!string.Equals(income.ReportedCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            ViewBag.FetchNote = $"Revenue is reported in {income.ReportedCurrency}; left blank — enter the USD value manually.";

        return View("Create", model);
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

    [HttpPost, Route("{id:long}/delete", Name = "CompanyDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _companies.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    // FMP income was premium-gated (non-US). Pull revenue + gross margin from Yahoo Finance,
    // converting revenue to USD via ExchangeRate-API. Margin is a ratio so it's filled regardless
    // of currency; revenue is left blank only if the currency can't be converted at all.
    private async Task ApplyYahooFallback(CompanyCreateModel model, string ticker)
    {
        var yf = await _yahoo.GetFinancialsAsync(ticker);
        if (yf == null)
        {
            ViewBag.FetchNote = "Financials aren't available for this symbol — enter revenue and gross margin manually.";
            return;
        }

        if (yf.GrossMargins is { } gm)
            model.GrossMargin = Math.Round(gm, 2); // 2 dp to satisfy the form's step="0.01"

        if (yf.Revenue is { } rev && !string.IsNullOrWhiteSpace(yf.Currency))
        {
            var rate = await _exchangeRate.GetUsdRateAsync(yf.Currency);
            if (rate is { } r)
                model.RevenueTotal = Math.Round(rev * r);
            else
                ViewBag.FetchNote = $"Revenue is in {yf.Currency} (no USD conversion rate available) — enter it manually.";
        }

        ViewBag.FetchNote ??= "Financials filled from Yahoo Finance (FMP is premium-gated for this symbol).";
    }

    // Find the Country matching FMP's ISO-2 code; if absent, look it up on REST Countries and
    // create the row. Re-checks by cca2/cca3/name before inserting so a hand-entered country
    // (which may use a different code format) isn't duplicated. Anything unresolved -> null
    // (the user leaves it blank or picks one).
    private async Task<Country?> ResolveOrCreateCountry(string? iso2)
    {
        if (string.IsNullOrWhiteSpace(iso2)) return null;

        var existing = _countries.GetAll().ToList();
        var match = existing.FirstOrDefault(c => string.Equals(c.Code, iso2, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        RestCountry? rc;
        try { rc = await _restCountries.GetByCodeAsync(iso2); }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException) { return null; }
        if (rc == null) return null;

        match = existing.FirstOrDefault(c =>
            string.Equals(c.Code, rc.Cca2, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Code, rc.Cca3, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(c.Name, rc.Name?.Common, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        var created = new Country(
            rc.Cca2 ?? iso2,
            rc.Name?.Common ?? iso2,
            rc.Region ?? "",
            rc.Currencies?.Keys.FirstOrDefault() ?? "")
        {
            Population = rc.Population
        };
        _countries.Add(created);
        return created;
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
