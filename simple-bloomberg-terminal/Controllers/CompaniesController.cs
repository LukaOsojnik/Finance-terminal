using System.Text.Json;
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
    private readonly IDeepSeekClient _deepSeek;
    private readonly string _deepSeekModel;

    public CompaniesController(ICompanyRepository companies, ICountryRepository countries,
        IFmpApiClient fmp, IRestCountriesClient restCountries,
        IYahooFinanceClient yahoo, IExchangeRateApiClient exchangeRate,
        IDeepSeekClient deepSeek, IConfiguration config)
    {
        _companies = companies;
        _countries = countries;
        _fmp = fmp;
        _restCountries = restCountries;
        _yahoo = yahoo;
        _exchangeRate = exchangeRate;
        _deepSeek = deepSeek;
        _deepSeekModel = config["DeepSeek:ScanModel"] ?? "deepseek-v4-flash";
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
            CompanyRisks = company.CompanyRisks.Where(r => r.DeletedAt == null),
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

        // Stamp the fetch date so the row always has an "as of" without manual entry.
        model.AsOf = DateOnly.FromDateTime(DateTime.Today);

        // The static label map only covers common FMP/Yahoo labels; on a miss, let DeepSeek pick
        // the industry within the already-resolved sector so the user doesn't have to choose.
        if (model.Industry == null)
            await ResolveIndustryWithLlm(model, profile);

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

        // Flags the banner + per-field "auto-filled" glow on the Create view.
        ViewBag.Fetched = true;
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

    // Returns status codes (not a redirect) because the row-delete is driven by fetch in site.js;
    // a redirect would resolve to a 200 page and the JS would treat a blocked delete as success.
    [HttpPost, Route("{id:long}/delete", Name = "CompanyDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _companies.SoftDelete(id); }
        catch (InvalidOperationException ex) { return Conflict(ex.Message); }
        return Ok();
    }

    // Feeds the "linked sources" popup shown when a delete is blocked. Returns both directions:
    // "owned" = this company's own sources (these block deletion); "inverse" = sources owned by
    // OTHER companies that point at this one. Both are soft-deleted via the source APIs from the UI.
    [HttpGet, Route("{id:long}/linked-sources")]
    public IActionResult LinkedSources(long id)
    {
        var c = _companies.GetWithGraphRelations(id);
        if (c == null) return NotFound();

        var owned = c.RevenueSources.Where(r => r.DeletedAt == null)
                .Select(r => new { kind = "revenue", direction = "owned", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.RelatedCompany?.Name })
            .Concat(c.CostSources.Where(s => s.DeletedAt == null)
                .Select(s => new { kind = "cost", direction = "owned", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.RelatedCompany?.Name }));

        var inverse = c.RevenueFromDependents.Where(r => r.DeletedAt == null)
                .Select(r => new { kind = "revenue", direction = "inverse", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.Company?.Name })
            .Concat(c.CostFromDependents.Where(s => s.DeletedAt == null)
                .Select(s => new { kind = "cost", direction = "inverse", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.Company?.Name }));

        return Json(new { owned, inverse });
    }

    // Async fallback for industry: the static map missed FMP's free-form label, so ask DeepSeek to
    // choose one GICS industry from those belonging to the already-resolved sector. Constraining the
    // candidates to that sector keeps the model honest and the result self-consistent (industry's
    // sector == model.Sector). Any failure leaves Industry null and the user picks it on review.
    private async Task ResolveIndustryWithLlm(CompanyCreateModel model, FmpProfile profile)
    {
        var candidates = Enum.GetValues<GicsIndustry>()
            .Where(i => i.GetSector() == model.Sector)
            .ToList();
        if (candidates.Count == 0) return;

        var allowed = string.Join(", ", candidates.Select(c => c.ToString()));
        var system = "You classify a company into exactly one GICS industry. Reply ONLY with JSON " +
            "{\"industry\":\"ENUM_NAME\"} where ENUM_NAME is exactly one of the allowed values, " +
            "or {\"industry\":null} if none fit.";
        var user = $"Company: {profile.CompanyName}\nSector: {model.Sector}\n" +
            $"Source industry label: {profile.Industry}\nAllowed industries: {allowed}";

        string raw;
        try { raw = await _deepSeek.CompleteAsync(_deepSeekModel, system, user, maxTokens: 100, jsonObject: true); }
        catch (HttpRequestException) { return; }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("industry", out var el) &&
                el.ValueKind == JsonValueKind.String &&
                Enum.TryParse<GicsIndustry>(el.GetString(), out var parsed) &&
                parsed.GetSector() == model.Sector)
                model.Industry = parsed;
        }
        catch (JsonException) { /* malformed reply -> leave Industry null */ }
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
