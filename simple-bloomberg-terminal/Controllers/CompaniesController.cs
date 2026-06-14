using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

[Route("companies")]
// Any authenticated user — keyed actions (FMP fetch, private discovery, rediscover) run on the
// USER's own API keys. Browse actions stay [AllowAnonymous] (overrides below). A missing key shows
// the "add your key" popup; logged-out callers get the sign-in prompt.
[Authorize]
public class CompaniesController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;
    private readonly IFmpApiClient _fmp;
    private readonly IRestCountriesClient _restCountries;
    private readonly ITickerProfileEnricher _enricher;
    private readonly ICompanyFinancialsService _financials;
    private readonly IStockApiClient _stock;
    private readonly ICompanyProfileDiscovery _profileDiscovery;
    private readonly IIndustryClassifier _industryClassifier;
    private readonly RediscoverJobStore _rediscoverJobs;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IUserApiKeyProvider _keys;

    public CompaniesController(ICompanyRepository companies, ICountryRepository countries,
        IFmpApiClient fmp, IRestCountriesClient restCountries,
        ITickerProfileEnricher enricher,
        ICompanyFinancialsService financials, IStockApiClient stock,
        ICompanyProfileDiscovery profileDiscovery, IIndustryClassifier industryClassifier,
        RediscoverJobStore rediscoverJobs, IServiceScopeFactory scopeFactory, IUserApiKeyProvider keys)
    {
        _rediscoverJobs = rediscoverJobs;
        _scopeFactory = scopeFactory;
        _keys = keys;
        _companies = companies;
        _countries = countries;
        _fmp = fmp;
        _restCountries = restCountries;
        _enricher = enricher;
        _financials = financials;
        _stock = stock;
        _profileDiscovery = profileDiscovery;
        _industryClassifier = industryClassifier;
    }

    [AllowAnonymous]
    [HttpGet, Route("")]
    public IActionResult Index() => View(_companies.GetAll());

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) =>
        PartialView("_TableBody", _companies.Search(term));

    [AllowAnonymous]
    [HttpGet, Route("lookup")]
    public IActionResult Lookup(string? term) =>
        Json(_companies.Lookup(term).Take(10).Select(c => new { id = c.Id, label = c.Name }));

    [AllowAnonymous]
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
            // Status filter hides Pending user contributions from the public profile until a Manager
            // approves them; Approved is the default so all existing/admin data still shows.
            RevenueSources = company.RevenueSources.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved),
            CostSources = company.CostSources.Where(c => c.DeletedAt == null && c.Status == ContributionStatus.Approved),
            CompanyRisks = company.CompanyRisks.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved),
            Financials = company.Financials.Where(f => f.DeletedAt == null)
                .OrderByDescending(f => f.EndDate ?? DateOnly.FromDateTime(DateTime.MinValue))
                .ThenByDescending(f => f.FiscalYear),
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
        CompanyCreateModel? model;
        try { model = await BuildModelFromTickerAsync(ticker); }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "FMP is unreachable. Try again or enter the company manually.");
            return View("Create", new CompanyCreateModel());
        }
        // No FMP key on this account: this is a full-page form, so surface guidance inline rather
        // than the AJAX popup the keyed buttons get.
        catch (MissingApiKeyException ex)
        {
            ModelState.AddModelError("", $"{ex.Message}. Add your FMP API key under Profile ▸ API Keys, then try again.");
            return View("Create", new CompanyCreateModel());
        }

        if (model is null)
        {
            ModelState.AddModelError("", $"No company found for ticker '{ticker}'.");
            return View("Create", new CompanyCreateModel());
        }

        // Flags the banner + per-field "auto-filled" glow on the Create view.
        ViewBag.Fetched = true;
        return View("Create", model);
    }

    // Shared real-API fetch used by both the New Company prefill (Fetch) and the bulk Backfill: pull
    // FMP profile + income, map to a CompanyCreateModel, resolve industry (LLM fallback) and country,
    // and backfill financials from Yahoo when FMP income is gated. Returns null if the profile is
    // missing/empty; lets an FMP transport error (incl. 402/429) bubble for the caller to handle.
    private async Task<CompanyCreateModel?> BuildModelFromTickerAsync(string ticker)
    {
        var profile = await _fmp.GetProfileAsync(ticker);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName)) return null;

        // Financials are a premium endpoint for some symbols (e.g. non-US return HTTP 402), so
        // treat income as optional — keep the profile-driven fields and leave financials blank.
        FmpIncome? income = null;
        try { income = await _fmp.GetLatestIncomeAsync(ticker); }
        catch (HttpRequestException) { /* income unavailable on this plan/symbol */ }

        // Shared kernel: maps the profile, stamps AsOf, resolves industry (LLM on a label miss) and
        // backfills Yahoo financials when income is gated. Country + the note are surfaced per-caller.
        var (model, note) = await _enricher.BuildModelAsync(profile, income, ticker);
        if (note != null) ViewBag.FetchNote = note;

        var country = await ResolveOrCreateCountry(profile.Country, _countries, _restCountries);
        if (country != null)
        {
            model.CountryId = country.Id;
            ViewBag.CountryLabel = country.Name;
        }

        return model;
    }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CompanyCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new Company(model.Name, model.CountryId, model.Sector)
        {
            Cik = model.Cik,
            Type = model.Type,
            Industry = model.Industry,
            RevenueTotal = model.RevenueTotal,
            GrossMargin = model.GrossMargin,
            MarketCap = model.MarketCap,
            AsOf = model.AsOf,
            Notes = model.Notes
        };
        _companies.Add(entity);

        // Prefilled from a ticker -> pull the dated financial history (FMP, or Yahoo fallback) and
        // store it against the new company. Best-effort: an API failure must not block the create.
        if (!string.IsNullOrWhiteSpace(model.Symbol))
        {
            try { _companies.ReplaceFinancials(entity.Id, await _financials.BuildAsync(entity.Id, model.Symbol)); }
            catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials unavailable — company still created */ }
        }
        // Private company with an AI-estimated revenue: store it as a single CLAUDE_ESTIMATED history
        // row so the Details matrix shows it clearly flagged as an estimate (not reported API data).
        else if (model.Type == CompanyType.PRIVATE && entity.RevenueTotal is { } rev)
        {
            var fy = entity.AsOf?.Year ?? DateTime.UtcNow.Year;
            _companies.ReplaceFinancials(entity.Id, [new CompanyFinancial(entity.Id, fy, FiscalPeriod.FY)
            {
                EndDate = entity.AsOf,
                Source = DataSource.CLAUDE_ESTIMATED,
                CapturedAt = DateTime.UtcNow,
                Revenue = rev,
                GrossMargin = entity.GrossMargin
            }]);
        }
        return RedirectToAction(nameof(Index));
    }

    // Prefill the create form for a PRIVATE company (no ticker) from a web-search profile. Like Fetch,
    // it saves nothing — the user reviews the AI-estimated fields before submitting the Create POST.
    [HttpPost, Route("discover-private"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscoverPrivate(string? name)
    {
        PopulateDropdowns();
        ViewBag.PrivateMode = true;

        if (string.IsNullOrWhiteSpace(name))
        {
            ModelState.AddModelError("", "Enter a company name.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE });
        }

        var companyName = name.Trim();
        CompanyProfileResult? result;
        try { result = await _profileDiscovery.DiscoverAsync(companyName); }
        catch (HttpRequestException)
        {
            ModelState.AddModelError("", "AI discovery is unreachable. Try again or enter the company manually.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }
        // No Perplexity key on this account: full-page form, so guide inline.
        catch (MissingApiKeyException ex)
        {
            ModelState.AddModelError("", $"{ex.Message}. Add your Perplexity API key under Profile ▸ API Keys, then try again.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }

        if (result is null)
        {
            ModelState.AddModelError("", $"Couldn't find a profile for '{companyName}'. Enter it manually.");
            return View("Create", new CompanyCreateModel { Type = CompanyType.PRIVATE, Name = companyName });
        }

        var (model, countryLabel) = await BuildPrivateModelAsync(result, companyName, _industryClassifier, _countries, _restCountries);
        if (countryLabel != null) ViewBag.CountryLabel = countryLabel;
        ViewBag.Fetched = true;
        ViewBag.FetchNote = "Profile and financials are AI-estimated from web search — review before saving.";
        return View("Create", model);
    }

    // Re-run Perplexity discovery for an EXISTING private company and overwrite its profile fields in
    // place (the Details "RE-DISCOVER" button). Detached: the ~90s sonar-pro call + LLM industry +
    // country lookup run on a background task with a fresh DI scope (the request-scoped DbContext is
    // gone once this returns), so a slow/unreliable web search can't time out the request. Returns a
    // job id the bottom-right widget polls. Public companies are rejected — they have a ticker.
    [HttpPost, Route("{id:long}/rediscover", Name = "CompanyRediscover"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Rediscover(long id)
    {
        var entity = _companies.GetById(id);
        if (entity == null) return NotFound();
        if (entity.Type != CompanyType.PRIVATE) return BadRequest("Re-discovery is for private companies only.");

        // Re-discovery runs Perplexity sonar. Verify the user's key now (throw -> 424 popup) and
        // snapshot the keys so the detached background scope can use them (it has no HttpContext).
        var keys = await _keys.GetAsync();
        if (string.IsNullOrWhiteSpace(keys.Perplexity)) throw MissingApiKeyException.Perplexity();

        var job = new RediscoverJob { CompanyId = id, CompanyName = entity.Name };
        _rediscoverJobs.Add(job);

        _ = Task.Run(async () =>
        {
            using var scope = _scopeFactory.CreateScope();
            var sp = scope.ServiceProvider;
            sp.GetRequiredService<IUserApiKeyProvider>().Set(keys);
            var companies = sp.GetRequiredService<ICompanyRepository>();
            var discovery = sp.GetRequiredService<ICompanyProfileDiscovery>();
            var classifier = sp.GetRequiredService<IIndustryClassifier>();
            var countries = sp.GetRequiredService<ICountryRepository>();
            var restCountries = sp.GetRequiredService<IRestCountriesClient>();
            try
            {
                var company = companies.GetById(job.CompanyId);
                if (company is null) { job.Status = ScanJobStatus.Error; job.Error = "Company no longer exists."; return; }

                job.Progress = "Searching the web with sonar-pro…";
                var result = await discovery.DiscoverAsync(company.Name);
                if (result is null) { job.Status = ScanJobStatus.Error; job.Error = "No profile found from web search."; return; }

                job.Progress = "Mapping sector / industry / country…";
                var (model, _) = await BuildPrivateModelAsync(result, company.Name, classifier, countries, restCountries);

                // Park the proposal for the user to judge in the widget — DO NOT save yet. ACCEPT applies
                // this; REJECT dismisses it. Summarise proposed-vs-current so the verdict is informed.
                static string B(double? v) => v is { } x ? "$" + (x / 1e9).ToString("F2") + "B" : "—";
                static string P(double? v) => v is { } x ? (x * 100).ToString("0.#") + "%" : "—";
                var yr = model.AsOf?.Year;
                job.Proposed = model;
                job.Sources = result.Sources;
                job.Result =
                    $"Proposed: rev {B(model.RevenueTotal)}{(yr is { } y ? $" (FY{y})" : "")}, margin {P(model.GrossMargin)}, " +
                    $"valuation {B(model.MarketCap)}, " +
                    $"{model.Sector.ToString().Replace('_', ' ')}{(model.Industry is { } ind ? " / " + ind.ToString().Replace('_', ' ') : "")}. " +
                    $"Current: rev {B(company.RevenueTotal)}, margin {P(company.GrossMargin)}, valuation {B(company.MarketCap)}.";

                job.Progress = "";
                job.Status = ScanJobStatus.Done;   // ready for review (not saved)
            }
            catch (Exception ex)
            {
                job.Status = ScanJobStatus.Error;
                job.Error = ex.Message;
                job.Progress = "";
            }
            finally
            {
                job.CompletedAt = DateTimeOffset.UtcNow;
            }
        });

        return Json(new { jobId = job.Id });
    }

    // ACCEPT a finished re-discovery from the widget: apply its parked proposal to the company and save.
    // Idempotent. REJECT has no endpoint — the widget just dismisses the job (nothing was written).
    [HttpPost, Route("rediscover/{jobId}/accept", Name = "CompanyRediscoverAccept"), ValidateAntiForgeryToken]
    public IActionResult AcceptRediscover(string jobId)
    {
        var job = _rediscoverJobs.Get(jobId);
        if (job?.Proposed is null) return NotFound();
        if (job.Applied) return Ok();   // already accepted — no-op

        var company = _companies.GetById(job.CompanyId);
        if (company is null) return NotFound();

        var model = job.Proposed;
        ApplyFetchedData(company, model);
        // Mirror the background rule: a fresh revenue carries a fresh margin, so overwrite the old margin
        // too (even with null) rather than letting ApplyFetchedData keep a stale guess.
        if (model.RevenueTotal is not null) company.GrossMargin = model.GrossMargin;
        _companies.Update(company);

        // Re-stamp the single estimated FY row so the Details matrix matches the accepted revenue/margin.
        if (company.RevenueTotal is { } rev)
        {
            var fy = company.AsOf?.Year ?? DateTime.UtcNow.Year;
            _companies.ReplaceFinancials(company.Id, [new CompanyFinancial(company.Id, fy, FiscalPeriod.FY)
            {
                EndDate = company.AsOf,
                Source = DataSource.CLAUDE_ESTIMATED,
                CapturedAt = DateTime.UtcNow,
                Revenue = rev,
                GrossMargin = company.GrossMargin
            }]);
        }

        job.Applied = true;
        return Ok();
    }

    // Map a discovered private-company profile onto the create model: sector by enum name (FMP-label
    // fallback), industry via the shared label map then LLM, country resolved/created, and the
    // estimated revenue + gross margin filled for review. Static + service params so it runs both on
    // the request (DiscoverPrivate) and on the detached re-discovery task. Returns the resolved
    // country name for the caller to surface (the request path puts it in ViewBag.CountryLabel).
    private static async Task<(CompanyCreateModel Model, string? CountryLabel)> BuildPrivateModelAsync(
        CompanyProfileResult r, string fallbackName,
        IIndustryClassifier classifier, ICountryRepository countries, IRestCountriesClient restCountries)
    {
        var model = new CompanyCreateModel
        {
            Type = CompanyType.PRIVATE,
            Name = r.Name ?? fallbackName,
            Notes = r.Description is { Length: > 2000 } d ? d[..2000] : r.Description,
            // Date the row to the year the revenue figure is from (so the FY row reads e.g. FY2024),
            // falling back to today when sonar couldn't pin a year.
            AsOf = r.RevenueYear is { } yr ? new DateOnly(yr, 12, 31) : DateOnly.FromDateTime(DateTime.Today),
            // sonar returns the exact GICS sector enum name; fall back to FMP's label map, then a default.
            Sector = (Enum.TryParse<Sector>(r.Sector, ignoreCase: true, out var sec) ? sec : (Sector?)null)
                     ?? FmpMapper.MapSector(r.Sector) ?? Sector.INFORMATION_TECHNOLOGY,
            Industry = FmpMapper.MapIndustry(r.Industry),
            RevenueTotal = r.RevenueUsd,
            MarketCap = r.ValuationUsd,   // private-company "market cap" = latest valuation
            GrossMargin = r.GrossMargin is { } gm ? Math.Round(gm, 2) : null
        };

        if (model.Industry == null)
            await ResolveIndustryWithLlm(model, model.Name, r.Industry, classifier);

        var country = await ResolveOrCreateCountry(r.CountryCode, countries, restCountries);
        if (country != null)
            model.CountryId = country.Id;
        return (model, country?.Name);
    }

    // Bulk-refresh existing companies from the real APIs (most were AI-seeded). Resolves a ticker
    // from each company's SEC CIK, skips any that already have financial history, then re-runs the
    // same fetch the New Company form does — overwriting the profile fields AND pulling the dated
    // financials. Stops cleanly when FMP's daily quota (HTTP 429) is hit, so a second click the next
    // day resumes with the still-missing companies. Returns a JSON summary for the results popup.
    [HttpPost, Route("backfill"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Backfill()
    {
        IReadOnlyDictionary<string, string> tickerMap;
        try { tickerMap = await _stock.GetCikTickerMap(); }
        catch (HttpRequestException) { return StatusCode(503, "SEC ticker map is unreachable. Try again."); }

        var withFmpFinancials = _companies.CompanyIdsWithFmpFinancials();
        var eligible = _companies.GetAll()
            .Where(c => !withFmpFinancials.Contains(c.Id))
            .Select(c => (Company: c, Ticker: ResolveTickerForCik(c.Cik, tickerMap)))
            .Where(x => x.Ticker != null)
            .ToList();

        var filled = new List<object>();
        var failed = new List<object>();
        var rateLimited = false;

        foreach (var (company, ticker) in eligible)
        {
            try
            {
                var model = await BuildModelFromTickerAsync(ticker!);
                if (model is null) { failed.Add(new { company.Name, ticker, reason = "no FMP profile" }); continue; }

                ApplyFetchedData(company, model);
                _companies.Update(company);

                var fin = await _financials.BuildAsync(company.Id, ticker!);
                _companies.ReplaceFinancials(company.Id, fin);
                filled.Add(new { company.Name, ticker, rows = fin.Count, source = fin.FirstOrDefault()?.Source.ToString() ?? "—" });
            }
            // FMP daily cap -> stop so a second run tomorrow resumes with the rest.
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                break;
            }
            catch (HttpRequestException ex)
            {
                failed.Add(new { company.Name, ticker, reason = $"FMP {(int?)ex.StatusCode}" });
            }
        }

        var remaining = eligible.Count - filled.Count - failed.Count;

        // Industry is independent of the FMP fetch: the LLM classifier works from name + sector alone,
        // so resolve it for EVERY company still missing one — including those the loop above never
        // touched (already-financialed and thus skipped, or non-US / no SEC CIK so no ticker, or left
        // unprocessed when an FMP 429 broke the loop). Constrained to the sector's GICS industries, so
        // it can only land a valid in-sector value. Best-effort per company; a classify miss leaves null.
        var industriesFilled = new List<object>();
        foreach (var c in _companies.GetAll().Where(c => c.Industry == null))
        {
            if (await _industryClassifier.ClassifyAsync(c.Sector, c.Name, null) is { } ind)
            {
                c.Industry = ind;
                _companies.Update(c);
                industriesFilled.Add(new { c.Name, industry = ind.ToString().Replace('_', ' ') });
            }
        }

        return Json(new
        {
            filled,
            failed,
            industriesFilled,
            rateLimited,
            remaining,
            message = rateLimited
                ? $"Filled {filled.Count}. FMP daily limit reached — {remaining} eligible companies remain; click again tomorrow. Resolved {industriesFilled.Count} industries."
                : $"Filled {filled.Count}, {failed.Count} failed, {remaining} eligible remaining. Resolved {industriesFilled.Count} industries."
        });
    }

    // Map a company's stored CIK to its primary ticker via the SEC map. Null for a missing or
    // placeholder (all-zeros) CIK, or one not in the map (e.g. a delisted ADR).
    private static string? ResolveTickerForCik(string? cik, IReadOnlyDictionary<string, string> map)
    {
        if (string.IsNullOrWhiteSpace(cik)) return null;
        var digits = new string(cik.Where(char.IsDigit).ToArray()).PadLeft(10, '0');
        if (digits.Trim('0').Length == 0) return null; // placeholder 0000000000 (non-US, no SEC CIK)
        return map.TryGetValue(digits, out var t) ? t : null;
    }

    // Overwrite the AI-seeded data fields with the real fetched values; preserve the curated Name,
    // and keep an existing value when the API returned none (?? on the optional fields).
    private static void ApplyFetchedData(Company e, CompanyCreateModel m)
    {
        e.Cik = m.Cik ?? e.Cik;
        e.Sector = m.Sector;
        e.Industry = m.Industry ?? e.Industry;
        e.MarketCap = m.MarketCap ?? e.MarketCap;
        e.RevenueTotal = m.RevenueTotal ?? e.RevenueTotal;
        e.GrossMargin = m.GrossMargin ?? e.GrossMargin;
        e.AsOf = m.AsOf ?? e.AsOf;
        e.Notes = string.IsNullOrWhiteSpace(m.Notes) ? e.Notes : m.Notes;
        if (m.CountryId > 0) e.CountryId = m.CountryId;
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
    [AllowAnonymous]
    [HttpGet, Route("{id:long}/linked-sources")]
    public IActionResult LinkedSources(long id)
    {
        var c = _companies.GetWithGraphRelations(id);
        if (c == null) return NotFound();

        var owned = c.RevenueSources.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved)
                .Select(r => new { kind = "revenue", direction = "owned", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.RelatedCompany?.Name })
            .Concat(c.CostSources.Where(s => s.DeletedAt == null && s.Status == ContributionStatus.Approved)
                .Select(s => new { kind = "cost", direction = "owned", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.RelatedCompany?.Name }));

        var inverse = c.RevenueFromDependents.Where(r => r.DeletedAt == null && r.Status == ContributionStatus.Approved)
                .Select(r => new { kind = "revenue", direction = "inverse", id = r.Id, name = r.Name, type = r.SourceType.ToString(), value = r.Value, other = r.Company?.Name })
            .Concat(c.CostFromDependents.Where(s => s.DeletedAt == null && s.Status == ContributionStatus.Approved)
                .Select(s => new { kind = "cost", direction = "inverse", id = s.Id, name = s.Name, type = s.CostBase.ToString(), value = s.Value, other = s.Company?.Name }));

        return Json(new { owned, inverse });
    }

    // Classify the company into one GICS industry within its already-resolved sector via the shared
    // classifier. Works from a plain name + source label so it serves the private-company discovery
    // (whose label comes from sonar). Leaves Industry null on a miss.
    private static async Task ResolveIndustryWithLlm(CompanyCreateModel model, string? companyName, string? sourceLabel, IIndustryClassifier classifier)
    {
        if (await classifier.ClassifyAsync(model.Sector, companyName, sourceLabel) is { } industry)
            model.Industry = industry;
    }

    // Find the Country matching FMP's ISO-2 code; if absent, look it up on REST Countries and
    // create the row. Re-checks by cca2/cca3/name before inserting so a hand-entered country
    // (which may use a different code format) isn't duplicated. Anything unresolved -> null
    // (the user leaves it blank or picks one).
    private static async Task<Country?> ResolveOrCreateCountry(string? iso2, ICountryRepository countries, IRestCountriesClient restCountries)
    {
        if (string.IsNullOrWhiteSpace(iso2)) return null;

        var existing = countries.GetAll().ToList();
        var match = existing.FirstOrDefault(c => string.Equals(c.Code, iso2, StringComparison.OrdinalIgnoreCase));
        if (match != null) return match;

        RestCountry? rc;
        try { rc = await restCountries.GetByCodeAsync(iso2); }
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
        countries.Add(created);
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
        Type = c.Type,
        CountryId = c.CountryId,
        Sector = c.Sector,
        Industry = c.Industry,
        RevenueTotal = c.RevenueTotal,
        GrossMargin = c.GrossMargin,
        MarketCap = c.MarketCap,
        AsOf = c.AsOf,
        Notes = c.Notes
    };

    private static void ApplyEdit(Company c, CompanyEditModel m)
    {
        c.Name = m.Name;
        c.Cik = m.Cik;
        c.Type = m.Type;
        c.CountryId = m.CountryId;
        c.Sector = m.Sector;
        c.Industry = m.Industry;
        c.RevenueTotal = m.RevenueTotal;
        c.GrossMargin = m.GrossMargin;
        c.MarketCap = m.MarketCap;
        c.AsOf = m.AsOf;
        c.Notes = m.Notes;
    }
}
