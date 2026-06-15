using System.Net;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>A create-form draft built from a real or web-searched profile: the mapped model plus the
/// resolved country name and an optional note for the caller to surface (the controller decides what
/// becomes ViewBag — the service never touches MVC).</summary>
public record CompanyDraft(CompanyCreateModel Model, string? CountryLabel, string? Note);

/// <summary>The outcome of a bulk <see cref="ICompanyProvisioningService.BackfillAsync"/> run, shaped
/// for the results popup. Lists hold anonymous rows (name/ticker/reason) the controller serializes.</summary>
public record BackfillResult(
    IReadOnlyList<object> Filled,
    IReadOnlyList<object> Failed,
    IReadOnlyList<object> IndustriesFilled,
    bool RateLimited,
    int Remaining,
    string Message);

/// <summary>
/// Owns turning a ticker (FMP) or a web-searched name (Perplexity) into a company: the shared
/// FMP→enrich→financials→industry/country pipeline that both the New Company form
/// (<c>CompaniesController</c>) and the counterparty-link flow (<c>ExtractionController</c>) drive.
/// Lives here so that pipeline has one home rather than a copy per controller.
/// </summary>
public interface ICompanyProvisioningService
{
    // New Company prefill / bulk backfill: real FMP profile + income -> create model. Null when the
    // profile is missing/empty; an FMP transport error (incl. 402/429) bubbles for the caller.
    Task<CompanyDraft?> BuildFromTickerAsync(string ticker);

    // Private-company prefill / re-discovery: a web-searched profile -> create model.
    Task<CompanyDraft> BuildPrivateAsync(CompanyProfileResult result, string fallbackName);

    // Bulk-refresh existing companies from FMP (keyed off each company's SEC CIK) + resolve missing
    // industries. Throws HttpRequestException only if the SEC ticker map itself is unreachable.
    Task<BackfillResult> BackfillAsync();

    // Reuse (fuzzy name match) or create the counterparty company behind a confirmed link.
    Task<long> GetOrCreateCounterpartyAsync(LinkCounterpartyRequest req, Company owner);

    // Overwrite a company's AI-seeded fields with fetched values (preserve curated Name; keep an
    // existing value when the fetch returned none). Used by Backfill and the re-discovery accept.
    void ApplyFetchedData(Company entity, CompanyCreateModel model);
}

public class CompanyProvisioningService(
    ICompanyRepository companies,
    ICountryRepository countries,
    IFmpApiClient fmp,
    IRestCountriesClient restCountries,
    ITickerProfileEnricher enricher,
    ICompanyFinancialsService financials,
    IStockApiClient stock,
    IIndustryClassifier industryClassifier) : ICompanyProvisioningService
{
    public async Task<CompanyDraft?> BuildFromTickerAsync(string ticker)
    {
        var profile = await fmp.GetProfileAsync(ticker);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName)) return null;

        // Financials are a premium endpoint for some symbols (e.g. non-US return HTTP 402), so
        // treat income as optional — keep the profile-driven fields and leave financials blank.
        FmpIncome? income = null;
        try { income = await fmp.GetLatestIncomeAsync(ticker); }
        catch (HttpRequestException) { /* income unavailable on this plan/symbol */ }

        // Shared kernel: maps the profile, stamps AsOf, resolves industry (LLM on a label miss) and
        // backfills Yahoo financials when income is gated. Country + the note are surfaced per-caller.
        var (model, note) = await enricher.BuildModelAsync(profile, income, ticker);

        var country = await ResolveOrCreateCountry(profile.Country);
        if (country != null)
            model.CountryId = country.Id;

        return new CompanyDraft(model, country?.Name, note);
    }

    public async Task<CompanyDraft> BuildPrivateAsync(CompanyProfileResult r, string fallbackName)
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

        if (model.Industry == null && await industryClassifier.ClassifyAsync(model.Sector, model.Name, r.Industry) is { } industry)
            model.Industry = industry;

        var country = await ResolveOrCreateCountry(r.CountryCode);
        if (country != null)
            model.CountryId = country.Id;
        return new CompanyDraft(model, country?.Name, null);
    }

    public async Task<BackfillResult> BackfillAsync()
    {
        var tickerMap = await stock.GetCikTickerMap();   // HttpRequestException bubbles -> controller 503

        var withFmpFinancials = companies.CompanyIdsWithFmpFinancials();
        var eligible = companies.GetAll()
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
                var draft = await BuildFromTickerAsync(ticker!);
                if (draft is null) { failed.Add(new { company.Name, ticker, reason = "no FMP profile" }); continue; }

                ApplyFetchedData(company, draft.Model);
                companies.Update(company);

                var fin = await financials.BuildAsync(company.Id, ticker!);
                companies.ReplaceFinancials(company.Id, fin);
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
        foreach (var c in companies.GetAll().Where(c => c.Industry == null))
        {
            if (await industryClassifier.ClassifyAsync(c.Sector, c.Name, null) is { } ind)
            {
                c.Industry = ind;
                companies.Update(c);
                industriesFilled.Add(new { c.Name, industry = ind.ToString().Replace('_', ' ') });
            }
        }

        var message = rateLimited
            ? $"Filled {filled.Count}. FMP daily limit reached — {remaining} eligible companies remain; click again tomorrow. Resolved {industriesFilled.Count} industries."
            : $"Filled {filled.Count}, {failed.Count} failed, {remaining} eligible remaining. Resolved {industriesFilled.Count} industries.";

        return new BackfillResult(filled, failed, industriesFilled, rateLimited, remaining, message);
    }

    public async Task<long> GetOrCreateCounterpartyAsync(LinkCounterpartyRequest req, Company owner)
    {
        if (companies.MatchByName(req.Name) is { } existing) return existing.Id;

        if (!string.IsNullOrWhiteSpace(req.Ticker))
        {
            FmpProfile? profile = null;
            // FMP here is a best-effort enrichment of a linked counterparty — a missing user key (or
            // FMP being down) just falls through to the minimal stub below, never blocks the link.
            try { profile = await fmp.GetProfileAsync(req.Ticker.Trim()); }
            catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* -> stub below */ }

            if (profile is { CompanyName.Length: > 0 })
            {
                // sonar's name may differ from FMP's canonical one — re-check before inserting.
                if (companies.MatchByName(profile.CompanyName) is { } byFmp) return byFmp.Id;

                FmpIncome? income = null;
                try { income = await fmp.GetLatestIncomeAsync(req.Ticker.Trim()); }
                catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials optional */ }

                // Same enrichment kernel the New Company form uses: maps the profile, stamps AsOf,
                // resolves industry (LLM on a label miss) and backfills Yahoo financials when income is
                // gated. The note is form-only, so the link path discards it. Country falls back to the
                // owner's here (vs the form's resolve/create), so it stays a per-caller concern.
                var (model, _) = await enricher.BuildModelAsync(profile, income, req.Ticker.Trim());
                var entity = new Company(model.Name, ResolveCountryId(profile.Country, owner), model.Sector)
                {
                    Cik = model.Cik,
                    Industry = model.Industry,
                    RevenueTotal = model.RevenueTotal,
                    GrossMargin = model.GrossMargin,
                    MarketCap = model.MarketCap,
                    AsOf = model.AsOf,
                    Notes = model.Notes
                };
                companies.Add(entity);

                // Same dated financial history the New Company form pulls. Best-effort: a failure
                // (premium-gated, unreachable) must not block linking the counterparty.
                try { companies.ReplaceFinancials(entity.Id, await financials.BuildAsync(entity.Id, req.Ticker.Trim())); }
                catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials unavailable — company still created */ }
                return entity.Id;
            }
        }

        // No ticker / FMP miss: minimal company so the link still works. Previously this left Industry
        // null; resolve it within the sector (the same classifier the New Company form uses) so
        // ticker-less companies aren't stuck unclassified. No ticker => treat as private.
        var sec = ParseSector(req.Sector) ?? owner.Sector;
        var stub = new Company(req.Name, ResolveCountryId(req.CountryCode, owner), sec)
        {
            Type = string.IsNullOrWhiteSpace(req.Ticker) ? CompanyType.PRIVATE : CompanyType.PUBLIC,
            Industry = await industryClassifier.ClassifyAsync(sec, req.Name, null)
        };
        companies.Add(stub);
        return stub.Id;
    }

    public void ApplyFetchedData(Company e, CompanyCreateModel m)
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

    // Map a company's stored CIK to its primary ticker via the SEC map. Null for a missing or
    // placeholder (all-zeros) CIK, or one not in the map (e.g. a delisted ADR).
    private static string? ResolveTickerForCik(string? cik, IReadOnlyDictionary<string, string> map)
    {
        var digits = Cik.Normalize(cik);
        if (digits is null || digits.Trim('0').Length == 0) return null; // missing or placeholder 0000000000 (non-US, no SEC CIK)
        return map.TryGetValue(digits, out var t) ? t : null;
    }

    // Find the Country matching FMP's ISO-2 code; if absent, look it up on REST Countries and
    // create the row. Re-checks by cca2/cca3/name before inserting so a hand-entered country
    // (which may use a different code format) isn't duplicated. Anything unresolved -> null
    // (the user leaves it blank or picks one).
    private async Task<Country?> ResolveOrCreateCountry(string? iso2)
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

    // Match an ISO-2 code against existing countries; fall back to the inspecting company's country
    // (always valid). Avoids creating a half-populated Country row for a counterparty.
    private long ResolveCountryId(string? iso2, Company owner) =>
        !string.IsNullOrWhiteSpace(iso2) &&
        countries.GetAll().FirstOrDefault(c => string.Equals(c.Code, iso2, StringComparison.OrdinalIgnoreCase)) is { } m
            ? m.Id : owner.CountryId;

    // sonar returns GICS display form ("Information Technology"); the enum uses "INFORMATION_TECHNOLOGY".
    private static Sector? ParseSector(string? sector)
    {
        var normalized = sector?.Trim().ToUpperInvariant().Replace(' ', '_');
        return Enum.TryParse<Sector>(normalized, true, out var s) ? s : null;
    }
}
