using System.Net;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>A create-form draft built from a real or web-searched profile: the mapped model plus the
/// resolved country name and an optional note for the caller to surface (the controller decides what
/// becomes ViewBag — the service never touches MVC).</summary>
public record CompanyDraft(CompanyCreateModel Model, string? CountryLabel, string? Note);

/// <summary>The outcome of a bulk backfill run (financials or industries), shaped for the results
/// popup. Lists hold anonymous rows (name/ticker/reason) the controller serializes; a given run fills
/// only its relevant lists (financials -> Filled/Failed, industries -> IndustriesFilled).</summary>
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

    // FAST provision (phase 1): create + persist a company from its FMP PROFILE ONLY — just the fields
    // an index needs (Name/CIK/Sector/Country/MarketCap), ~1 FMP call, no income/financials/industry-LLM/
    // Yahoo. Returns the new company, or null when FMP has no profile or the country can't be resolved.
    // Throws MissingApiKeyException / HttpRequestException (incl. 429) so a bulk caller can stop.
    Task<Company?> CreateFromProfileAsync(string ticker);

    // FULL enrichment (phase 2): fill the deferred data — dated financial history (FMP, Yahoo fallback)
    // and the GICS industry (LLM) — for already-created companies. Best-effort per company; stops on an
    // FMP 429 / missing key. Run in the background after a fast index import has connected the members.
    Task EnrichAsync(IEnumerable<long> companyIds, IProgress<string>? progress = null);

    // Private-company prefill / re-discovery: a web-searched profile -> create model.
    Task<CompanyDraft> BuildPrivateAsync(CompanyProfileResult result, string fallbackName);

    // Backfill #1 — real financial HISTORY from FMP (Yahoo fallback) for companies that don't have FMP
    // financials yet, refreshing their profile fields along the way. FMP-bound: stops cleanly on the daily
    // 429 so a re-run tomorrow resumes. Throws HttpRequestException only if the SEC ticker map is unreachable.
    Task<BackfillResult> BackfillFinancialsAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    // Backfill #2 — resolve the GICS sub-industry (+ Industry rollup) for every company still missing one.
    // LLM-bound (cheap tier, cache-deduped); re-fetches the FMP label when absent (skipped on a 429). Both
    // report a line per company for the live view and honor cancellation (aborts the in-flight LLM call).
    Task<BackfillResult> BackfillIndustriesAsync(IProgress<string>? progress = null, CancellationToken ct = default);

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
    IIndustryClassifier industryClassifier,
    ILogger<CompanyProvisioningService> logger) : ICompanyProvisioningService
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

    public async Task<Company?> CreateFromProfileAsync(string ticker)
    {
        var profile = await fmp.GetProfileAsync(ticker);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName)) return null;

        // Country is a required FK; if FMP's code can't be resolved/created, skip rather than risk an
        // FK violation. Real listed companies always carry one, so this only drops genuine edge cases.
        var country = await ResolveOrCreateCountry(profile.Country);
        if (country is null) return null;

        // Profile-only map (no income): just the membership-relevant fields. The raw FMP industry label
        // is stored now (no LLM/cost); the GICS sub-industry + Industry rollup are filled by EnrichAsync,
        // so they stay null on the model and ToEntity copies them through as null.
        var m = FmpMapper.ToCreateModel(profile, null);
        m.AsOf ??= DateOnly.FromDateTime(DateTime.Today);   // income-less map leaves AsOf null; stamp the fetch date
        var entity = CompanyMapper.ToEntity(m, country.Id);
        companies.Add(entity);
        return entity;
    }

    public async Task EnrichAsync(IEnumerable<long> companyIds, IProgress<string>? progress = null)
    {
        var ids = companyIds.Distinct().ToList();
        if (ids.Count == 0) return;

        var tickerMap = await stock.GetCikTickerMap();   // cik -> ticker, for financials.BuildAsync
        var done = 0;
        foreach (var id in ids)
        {
            done++;
            var company = companies.GetById(id);
            if (company is null) continue;
            progress?.Report($"Filling financials {done}/{ids.Count} ({company.Name})…");
            try
            {
                if (ResolveTickerForCik(company.Cik, tickerMap) is { } ticker)
                    companies.ReplaceFinancials(company.Id, await financials.BuildAsync(company.Id, ticker));

                // Resolve the GICS sub-industry from the FMP label captured at provision time (cached,
                // else one cheap LLM call) and roll it up to Industry. No FMP re-fetch needed here.
                if (company.GicsSubIndustry is null
                    && await industryClassifier.ResolveSubIndustryAsync(company.Sector, company.FmpIndustry, company.Name) is { } sub)
                {
                    company.GicsSubIndustry = sub;
                    company.Industry = sub.GetIndustry();
                    companies.Update(company);
                }
            }
            // FMP daily cap -> stop; the rest stay un-enriched (a later Backfill picks them up).
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { break; }
            catch (MissingApiKeyException) { break; }
            catch (HttpRequestException) { }   // this one failed; keep going
        }
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
            // sonar's free-text industry string is the vendor label here; resolve it like an FMP label.
            FmpIndustry = string.IsNullOrWhiteSpace(r.Industry) ? null : r.Industry.Trim(),
            RevenueTotal = r.RevenueUsd,
            MarketCap = r.ValuationUsd,   // private-company "market cap" = latest valuation
            GrossMargin = r.GrossMargin is { } gm ? Math.Round(gm, 2) : null
        };

        model.GicsSubIndustry = await industryClassifier.ResolveSubIndustryAsync(model.Sector, model.FmpIndustry, model.Name);
        model.Industry = model.GicsSubIndustry?.GetIndustry();

        var country = await ResolveOrCreateCountry(r.CountryCode);
        if (country != null)
            model.CountryId = country.Id;
        return new CompanyDraft(model, country?.Name, null);
    }

    public async Task<BackfillResult> BackfillFinancialsAsync(IProgress<string>? progress = null, CancellationToken ct = default)
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
        progress?.Report($"Backfilling financials for up to {eligible.Count} companies…");
        var done = 0;

        foreach (var (company, ticker) in eligible)
        {
            if (ct.IsCancellationRequested) { progress?.Report($"Cancelled — stopped after {done} of {eligible.Count}."); break; }
            done++;
            try
            {
                var draft = await BuildFromTickerAsync(ticker!);
                if (draft is null)
                {
                    failed.Add(new { company.Name, ticker, reason = "no FMP profile" });
                    progress?.Report($"{done}/{eligible.Count}  {company.Name}  → no FMP profile");
                    continue;
                }

                ApplyFetchedData(company, draft.Model);
                companies.Update(company);

                var fin = await financials.BuildAsync(company.Id, ticker!);
                companies.ReplaceFinancials(company.Id, fin);
                var source = fin.FirstOrDefault()?.Source.ToString() ?? "—";
                filled.Add(new { company.Name, ticker, rows = fin.Count, source });
                progress?.Report($"{done}/{eligible.Count}  {company.Name} ({ticker})  → {fin.Count} rows ({source})");
            }
            // FMP daily cap -> stop so a second run tomorrow resumes with the rest.
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                progress?.Report("FMP daily limit reached — stopping. Re-run tomorrow to continue.");
                break;
            }
            catch (HttpRequestException ex)
            {
                failed.Add(new { company.Name, ticker, reason = $"FMP {(int?)ex.StatusCode}" });
                progress?.Report($"{done}/{eligible.Count}  {company.Name}  → FMP {(int?)ex.StatusCode}");
            }
        }

        var remaining = eligible.Count - filled.Count - failed.Count;
        var message = rateLimited
            ? $"Filled {filled.Count}. FMP daily limit reached — {remaining} eligible companies remain; click again tomorrow."
            : $"Filled {filled.Count}, {failed.Count} failed, {remaining} eligible remaining.";

        return new BackfillResult(filled, failed, [], rateLimited, remaining, message);
    }

    public async Task<BackfillResult> BackfillIndustriesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Resolve the GICS sub-industry for every company that still lacks one but already carries a
        // raw FMP label. Label-less rows are skipped on purpose: acquiring a label needs an FMP profile
        // call, and once the FMP daily quota is hit those re-fetches fail — resolving such rows from
        // name + sector alone gave weak, quota-blind guesses. Restricting to rows that already have an
        // FmpIndustry keeps this sweep purely LLM-bound (zero FMP dependency) and label-grounded;
        // labels themselves are captured by the financials backfill.
        var industriesFilled = new List<object>();
        var pending = companies.GetAll()
            .Where(c => c.GicsSubIndustry == null && !string.IsNullOrWhiteSpace(c.FmpIndustry))
            .ToList();
        logger.LogInformation("Backfill: resolving GICS sub-industry for {Count} companies with an FMP label.", pending.Count);
        progress?.Report($"Resolving GICS sub-industry for {pending.Count} companies…");
        var done = 0;
        foreach (var c in pending)
        {
            // Close button -> CTS cancelled: stop cleanly and return what's resolved so far.
            if (ct.IsCancellationRequested) { progress?.Report($"Cancelled — stopped after {done} of {pending.Count}."); break; }
            done++;

            var label = c.FmpIndustry!;   // non-null: guaranteed by the pending filter
            GicsSubIndustry? sub;
            // The cancellation token reaches the LLM HTTP call, so cancelling aborts the in-flight request.
            try { sub = await industryClassifier.ResolveSubIndustryAsync(c.Sector, c.FmpIndustry, c.Name, ct); }
            catch (OperationCanceledException) { progress?.Report($"Cancelled — stopped after {done - 1} of {pending.Count}."); break; }

            if (sub is { } resolved)
            {
                c.GicsSubIndustry = resolved;
                c.Industry = resolved.GetIndustry();
                companies.Update(c);
                industriesFilled.Add(new { c.Name, industry = resolved.GetIndustry().ToString().Replace('_', ' ') });
                logger.LogInformation("Backfill {Done}/{Total}: {Name} [{Sector}] label='{Label}' -> {Sub} ({Industry})",
                    done, pending.Count, c.Name, c.Sector, label, resolved, resolved.GetIndustry());
                progress?.Report($"{done}/{pending.Count}  {c.Name}  [{c.Sector.ToString().Replace('_', ' ')}]  " +
                    $"label='{label}' → {resolved.ToString().Replace("_SUB", "").Replace('_', ' ')}");
            }
            else
            {
                logger.LogInformation("Backfill {Done}/{Total}: {Name} [{Sector}] label='{Label}' -> no fit",
                    done, pending.Count, c.Name, c.Sector, label);
                progress?.Report($"{done}/{pending.Count}  {c.Name}  [{c.Sector.ToString().Replace('_', ' ')}]  label='{label}' → no fit");
            }
        }

        var message = $"Resolved {industriesFilled.Count} of {pending.Count} industries.";
        return new BackfillResult([], [], industriesFilled, false, pending.Count - industriesFilled.Count, message);
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
                // The CIK is the canonical join: a name match can't bridge an acronym↔legal-name gap
                // (the agent's "TSMC" never normalises to an existing "Taiwan Semiconductor Manufacturing
                // Company"), but their CIKs are identical. Check it before the name re-check / insert.
                if (companies.MatchByCik(profile.Cik) is { } byCik) return byCik.Id;
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
                var entity = CompanyMapper.ToEntity(model, ResolveCountryId(profile.Country, owner));
                companies.Add(entity);

                // Same dated financial history the New Company form pulls. Best-effort: a failure
                // (premium-gated, unreachable) must not block linking the counterparty.
                try { companies.ReplaceFinancials(entity.Id, await financials.BuildAsync(entity.Id, req.Ticker.Trim())); }
                catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials unavailable — company still created */ }
                return entity.Id;
            }
        }

        // No ticker / FMP miss: minimal company so the link still works. No vendor label here, so resolve
        // the sub-industry from name + sector alone (a cache miss -> one cheap LLM call) and roll it up,
        // so ticker-less companies aren't stuck unclassified. No ticker => treat as private.
        var sec = ParseSector(req.Sector) ?? owner.Sector;
        var stubSub = await industryClassifier.ResolveSubIndustryAsync(sec, null, req.Name);
        var stub = new Company(req.Name, ResolveCountryId(req.CountryCode, owner), sec)
        {
            Type = string.IsNullOrWhiteSpace(req.Ticker) ? CompanyType.PRIVATE : CompanyType.PUBLIC,
            GicsSubIndustry = stubSub,
            Industry = stubSub?.GetIndustry()
        };
        companies.Add(stub);
        return stub.Id;
    }

    public void ApplyFetchedData(Company e, CompanyCreateModel m) => CompanyMapper.Apply(e, m);

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
