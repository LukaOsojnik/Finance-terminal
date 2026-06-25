using System.Net;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Provisioning;

/// <summary>A create-form draft built from a real or web-searched profile: the mapped model plus the
/// resolved country name and an optional note for the caller to surface (the controller decides what
/// becomes ViewBag â€” the service never touches MVC).</summary>
public record CompanyDraft(CompanyCreateModel Model, string? CountryLabel, string? Note);

/// <summary>Outcome of a single-company weekly-volume ingest, so the controller can map it to the right
/// HTTP status / message: Ok (RowCount stored), CompanyNotFound (404), NoTicker (no SEC ticker â€” non-US),
/// NoData (Yahoo returned nothing).</summary>
public record VolumeIngestResult(VolumeIngestStatus Status, int RowCount);
public enum VolumeIngestStatus { Ok, CompanyNotFound, NoTicker, NoData }

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
/// FMPâ†’enrichâ†’financialsâ†’industry/country pipeline that both the New Company form
/// (<c>CompaniesController</c>) and the counterparty-link flow (<c>ExtractionController</c>) drive.
/// Lives here so that pipeline has one home rather than a copy per controller.
/// </summary>
public interface ICompanyProvisioningService
{
    // New Company prefill / bulk backfill: real FMP profile + income -> create model. Null when the
    // profile is missing/empty; an FMP transport error (incl. 402/429) bubbles for the caller.
    Task<CompanyDraft?> BuildFromTickerAsync(string ticker);

    // FAST provision (phase 1): create + persist a company from its FMP PROFILE ONLY â€” just the fields
    // an index needs (Name/CIK/Sector/Country/MarketCap), ~1 FMP call, no income/financials/industry-LLM/
    // Yahoo. Returns the new company, or null when FMP has no profile or the country can't be resolved.
    // Throws MissingApiKeyException / HttpRequestException (incl. 429) so a bulk caller can stop.
    Task<Company?> CreateFromProfileAsync(string ticker);

    // FULL enrichment (phase 2): fill the deferred data â€” dated financial history (FMP, Yahoo fallback)
    // and the GICS industry (LLM) â€” for already-created companies. Best-effort per company; stops on an
    // FMP 429 / missing key. Run in the background after a fast index import has connected the members.
    Task EnrichAsync(IEnumerable<long> companyIds, IProgress<string>? progress = null);

    // Private-company prefill / re-discovery: a web-searched profile -> create model.
    Task<CompanyDraft> BuildPrivateAsync(CompanyProfileResult result, string fallbackName);

    // Backfill #1 â€” real financial HISTORY from FMP (Yahoo fallback) for companies that don't have FMP
    // financials yet, refreshing their profile fields along the way. FMP-bound: stops cleanly on the daily
    // 429 so a re-run tomorrow resumes. Throws HttpRequestException only if the SEC ticker map is unreachable.
    Task<BackfillResult> BackfillFinancialsAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    // Backfill #2 â€” resolve the GICS sub-industry (+ Industry rollup) for every company still missing one.
    // LLM-bound (cheap tier, cache-deduped); re-fetches the FMP label when absent (skipped on a 429). Both
    // report a line per company for the live view and honor cancellation (aborts the in-flight LLM call).
    Task<BackfillResult> BackfillIndustriesAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    // Backfill #3 â€” assign a CIK to US companies missing one (FMP returns null for some US filers, e.g.
    // WEN). Pure SEC name match (no FMP/LLM, no quota), so it's instant and never rate-limited. Reports a
    // line per company filled; the IndustriesFilled list carries the (name, cik) pairs for the popup.
    Task<BackfillResult> BackfillCiksAsync(IProgress<string>? progress = null, CancellationToken ct = default);

    // Backfill a single company's weekly trading-volume history from Yahoo (range=max) and store it,
    // clear-reinserting the rows. Resolves the Yahoo symbol from the company's CIK via the SEC map, so
    // it only works for US filers (non-US / placeholder-CIK companies report NoTicker). Best-effort:
    // a Yahoo failure yields NoData. Drives the Details page "INGEST VOLUME" button.
    Task<VolumeIngestResult> IngestWeeklyVolumeAsync(long companyId);

    // Reuse (fuzzy name match) or create the counterparty company behind a confirmed link.
    Task<long> GetOrCreateCounterpartyAsync(LinkCounterpartyRequest req, Company owner);

    // Overwrite a company's AI-seeded fields with fetched values (preserve curated Name; keep an
    // existing value when the fetch returned none). Used by Backfill and the re-discovery accept.
    void ApplyFetchedData(Company entity, CompanyCreateModel model);

    // On-demand re-resolve of one company's GICS sub-industry (fetches a missing FMP label first, then
    // the full constrained -> unconstrained classify). Always reattempts â€” even a prior NoFit â€” and
    // self-heals Sector/Industry from the pick. Drives the "Resolve with AI" action on the Unclassified
    // page. Returns the resolved sub-industry, or null if it still can't be placed.
    Task<GicsSubIndustry?> ReclassifyAsync(long companyId, CancellationToken ct = default);
}

public class CompanyProvisioningService(
    ICompanyRepository companies,
    ICountryRepository countries,
    IFmpApiClient fmp,
    IRestCountriesClient restCountries,
    ITickerProfileEnricher enricher,
    ICompanyFinancialsService financials,
    IYahooFinanceClient yahoo,
    IStockApiClient stock,
    IIndustryClassifier industryClassifier,
    ILogger<CompanyProvisioningService> logger) : ICompanyProvisioningService
{
    public async Task<CompanyDraft?> BuildFromTickerAsync(string ticker)
    {
        var profile = await fmp.GetProfileAsync(ticker);
        if (profile is null || string.IsNullOrWhiteSpace(profile.CompanyName)) return null;

        // Financials are a premium endpoint for some symbols (e.g. non-US return HTTP 402), so
        // treat income as optional â€” keep the profile-driven fields and leave financials blank.
        FmpIncome? income = null;
        try { income = await fmp.GetLatestIncomeAsync(ticker); }
        catch (HttpRequestException) { /* income unavailable on this plan/symbol */ }

        // Shared kernel: maps the profile, stamps AsOf, resolves industry (LLM on a label miss) and
        // backfills Yahoo financials when income is gated. Country + the note are surfaced per-caller.
        var (model, note) = await enricher.BuildModelAsync(profile, income, ticker);
        model.Cik = await ResolveMissingCikAsync(model.Cik, ticker);

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
        m.Cik = await ResolveMissingCikAsync(m.Cik, ticker);
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
        var financialsStopped = false;   // FMP quota / missing key -> stop the DEPTH fetch, not the shallow one
        foreach (var id in ids)
        {
            done++;
            var company = companies.GetById(id);
            if (company is null) continue;
            progress?.Report($"Enriching {done}/{ids.Count} ({company.Name})â€¦");

            // SHALLOW (priority, no FMP): resolve the GICS sub-industry from the label already captured at
            // provision time â€” cache, else one cheap LLM call â€” and roll it up to Industry. Independent of
            // financials, so the FMP quota can never block classification; the classifier swallows its own
            // LLM/key errors and returns null.
            if (company.GicsSubIndustry is null)
            {
                var sub = await industryClassifier.ResolveSubIndustryAsync(
                    company.Sector, company.FmpIndustry, company.Name, company.Notes);
                // A hit rolls Industry + Sector up from the sub (self-heals a wrong/missing sector); a miss
                // flags NoFit so the Unclassified page can surface it for an AI re-resolve.
                ApplyClassification(company, sub);
                companies.Update(company);
            }

            // DEPTH (expensive FMP): dated financial history. Once the quota is hit (or no key) we stop
            // fetching financials but keep classifying the rest above; a later Backfill fills the remainder.
            if (financialsStopped) continue;
            try
            {
                if (ResolveTickerForCik(company.Cik, tickerMap) is { } ticker)
                    companies.ReplaceFinancials(company.Id, await financials.BuildAsync(company.Id, ticker));
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { financialsStopped = true; }
            catch (MissingApiKeyException) { financialsStopped = true; }
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
            // sonar returns the exact GICS sector enum name; fall back to FMP's label map, else null
            // (unclassified â€” the classifier's unconstrained fallback resolves it from the sub-industry).
            Sector = (Enum.TryParse<Sector>(r.Sector, ignoreCase: true, out var sec) ? sec : (Sector?)null)
                     ?? FmpMapper.MapSector(r.Sector),
            // sonar's free-text industry string is the vendor label here; resolve it like an FMP label.
            FmpIndustry = string.IsNullOrWhiteSpace(r.Industry) ? null : r.Industry.Trim(),
            RevenueTotal = r.RevenueUsd,
            MarketCap = r.ValuationUsd,   // private-company "market cap" = latest valuation
            GrossMargin = r.GrossMargin is { } gm ? Math.Round(gm, 2) : null
        };

        model.GicsSubIndustry = await industryClassifier.ResolveSubIndustryAsync(
            model.Sector, model.FmpIndustry, model.Name, model.Notes);
        model.Industry = model.GicsSubIndustry?.GetIndustry();
        // A fallback pick may belong to a different sector than the source guess â€” let the sub-industry
        // be authoritative so the saved row is self-consistent (sub -> industry -> sector all agree).
        if (model.GicsSubIndustry is { } picked) model.Sector = picked.GetSector();

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
        progress?.Report($"Backfilling financials for up to {eligible.Count} companiesâ€¦");
        var done = 0;

        foreach (var (company, ticker) in eligible)
        {
            if (ct.IsCancellationRequested) { progress?.Report($"Cancelled â€” stopped after {done} of {eligible.Count}."); break; }
            done++;
            try
            {
                var draft = await BuildFromTickerAsync(ticker!);
                if (draft is null)
                {
                    failed.Add(new { company.Name, ticker, reason = "no FMP profile" });
                    progress?.Report($"{done}/{eligible.Count}  {company.Name}  â†’ no FMP profile");
                    continue;
                }

                ApplyFetchedData(company, draft.Model);
                companies.Update(company);

                var fin = await financials.BuildAsync(company.Id, ticker!);
                companies.ReplaceFinancials(company.Id, fin);
                var source = fin.FirstOrDefault()?.Source.ToString() ?? "â€”";
                filled.Add(new { company.Name, ticker, rows = fin.Count, source });
                progress?.Report($"{done}/{eligible.Count}  {company.Name} ({ticker})  â†’ {fin.Count} rows ({source})");
            }
            // FMP daily cap -> stop so a second run tomorrow resumes with the rest.
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests)
            {
                rateLimited = true;
                progress?.Report("FMP daily limit reached â€” stopping. Re-run tomorrow to continue.");
                break;
            }
            catch (HttpRequestException ex)
            {
                failed.Add(new { company.Name, ticker, reason = $"FMP {(int?)ex.StatusCode}" });
                progress?.Report($"{done}/{eligible.Count}  {company.Name}  â†’ FMP {(int?)ex.StatusCode}");
            }
        }

        var remaining = eligible.Count - filled.Count - failed.Count;
        var message = rateLimited
            ? $"Filled {filled.Count}. FMP daily limit reached â€” {remaining} eligible companies remain; click again tomorrow."
            : $"Filled {filled.Count}, {failed.Count} failed, {remaining} eligible remaining.";

        return new BackfillResult(filled, failed, [], rateLimited, remaining, message);
    }

    public async Task<BackfillResult> BackfillIndustriesAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        // Self-contained per company: industry is the priority data and needs only ONE cheap FMP call
        // (the profile), so this sweep gets its own label rather than depending on the financials backfill
        // (which spends ~5 FMP calls/company on profile + four statement endpoints). For each company
        // missing a sub-industry:
        //   1. If it has no FMP label yet, fetch the FMP PROFILE (one call) to get it and store it.
        //   2. Resolve label -> GICS sub-industry (cache, else cheap LLM) and roll up to Industry.
        // A company we can't get a label for (no SEC ticker, or the FMP daily quota is spent) is skipped
        // rather than weak-guessed from name + sector. On the 429 we stop fetching new labels but keep
        // resolving rows that already carry one (cached/stored labels need no FMP call).
        // Only Pending rows: a row already ruled NoFit isn't weak-guessed again here â€” it waits for an
        // explicit AI re-resolve from the Unclassified page (ReclassifyAsync), so the automatic sweep
        // doesn't burn an LLM call per junk row every run.
        var pending = companies.GetAll()
            .Where(c => c.GicsSubIndustry == null && c.ClassifyStatus != ClassifyStatus.NoFit
                        && !c.ClassificationLocked).ToList();
        if (pending.Count == 0)
        {
            const string none = "All companies already have a GICS sub-industry â€” nothing to resolve.";
            progress?.Report(none);
            return new BackfillResult([], [], [], false, 0, none);
        }

        var tickerMap = await stock.GetCikTickerMap();   // for the per-company FMP-label fetch
        logger.LogInformation("Backfill: resolving GICS sub-industry for {Count} companies missing one.", pending.Count);
        progress?.Report($"Resolving GICS sub-industry for {pending.Count} companiesâ€¦");

        var industriesFilled = new List<object>();
        var rateLimited = false;
        var noFit = 0;
        var done = 0;
        foreach (var c in pending)
        {
            // Close button -> CTS cancelled: stop cleanly and return what's resolved so far.
            if (ct.IsCancellationRequested) { progress?.Report($"Cancelled â€” stopped after {done} of {pending.Count}."); break; }
            done++;

            // Step 1 â€” fetch the FMP label when missing (one profile call): the strongest classifier
            // signal and the dedup-cache key. Skipped once the daily FMP quota is hit, or with no ticker â€”
            // a label-less company is no longer skipped, it falls through to a name+description classify.
            if (string.IsNullOrWhiteSpace(c.FmpIndustry) && !rateLimited
                && ResolveTickerForCik(c.Cik, tickerMap) is { } t)
            {
                try
                {
                    if (await fmp.GetProfileAsync(t) is { Industry: { } lbl } && !string.IsNullOrWhiteSpace(lbl))
                    {
                        c.FmpIndustry = lbl.Trim();
                        companies.Update(c);   // persist the label even if the LLM step below is later cancelled
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { rateLimited = true; }
                catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* classify by name below */ }
            }

            // Step 2 â€” resolve: constrained to the source sector when known, else (or on a no-fit) an
            // unconstrained LLM pass over name + description + label. A hit rolls Industry AND Sector up
            // from the sub (self-healing a wrong/missing sector); a miss is flagged NoFit.
            GicsSubIndustry? sub;
            try { sub = await industryClassifier.ResolveSubIndustryAsync(c.Sector, c.FmpIndustry, c.Name, c.Notes, ct: ct); }
            catch (OperationCanceledException) { progress?.Report($"Cancelled â€” stopped after {done - 1} of {pending.Count}."); break; }

            ApplyClassification(c, sub);
            companies.Update(c);

            if (sub is { } resolved)
            {
                industriesFilled.Add(new { c.Name, industry = resolved.GetIndustry().ToString().Replace('_', ' ') });
                logger.LogInformation("Backfill {Done}/{Total}: {Name} [{Sector}] label='{Label}' -> {Sub} ({Industry})",
                    done, pending.Count, c.Name, c.Sector, c.FmpIndustry ?? "(none)", resolved, resolved.GetIndustry());
                progress?.Report($"{done}/{pending.Count}  {c.Name}  [{c.Sector?.ToString().Replace('_', ' ') ?? "â€”"}]  " +
                    $"â†’ {resolved.ToString().Replace("_SUB", "").Replace('_', ' ')}");
            }
            else
            {
                noFit++;
                logger.LogInformation("Backfill {Done}/{Total}: {Name} label='{Label}' -> no fit (flagged Unclassified)",
                    done, pending.Count, c.Name, c.FmpIndustry ?? "(none)");
                progress?.Report($"{done}/{pending.Count}  {c.Name}  â†’ no fit (flagged Unclassified)");
            }
        }

        var message = rateLimited
            ? $"Resolved {industriesFilled.Count} of {pending.Count}. FMP daily quota reached â€” re-run tomorrow to fetch the remaining labels."
            : $"Resolved {industriesFilled.Count} of {pending.Count}."
              + (noFit > 0 ? $" {noFit} couldn't be placed â€” see the Unclassified list." : "");
        return new BackfillResult([], [], industriesFilled, rateLimited, noFit, message);
    }

    public async Task<BackfillResult> BackfillCiksAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Loading SEC ticker mapâ€¦");
        var entries = await stock.GetTickerEntries();   // HttpRequestException bubbles -> controller "SEC unreachable"

        var filled = companies.BackfillUsCiksByName(
            entries.Select(e => (e.Title, Cik.Pad(e.CikStr.ToString()))));

        foreach (var (name, cik) in filled)
            progress?.Report($"{name}  â†’ CIK {cik}");

        var message = filled.Count == 0
            ? "No CIKs resolved â€” every US company already has one, or no SEC title matched its name."
            : $"Resolved {filled.Count} CIK{(filled.Count == 1 ? "" : "s")} from the SEC ticker map.";
        progress?.Report(message);
        return new BackfillResult([], [], filled.Cast<object>().ToList(), false, 0, message);
    }

    public async Task<long> GetOrCreateCounterpartyAsync(LinkCounterpartyRequest req, Company owner)
    {
        if (companies.MatchByName(req.Name) is { } existing) return existing.Id;

        if (!string.IsNullOrWhiteSpace(req.Ticker))
        {
            FmpProfile? profile = null;
            // FMP here is a best-effort enrichment of a linked counterparty â€” a missing user key (or
            // FMP being down) just falls through to the minimal stub below, never blocks the link.
            try { profile = await fmp.GetProfileAsync(req.Ticker.Trim()); }
            catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* -> stub below */ }

            if (profile is { CompanyName.Length: > 0 })
            {
                // The CIK is the canonical join: a name match can't bridge an acronymâ†”legal-name gap
                // (the agent's "TSMC" never normalises to an existing "Taiwan Semiconductor Manufacturing
                // Company"), but their CIKs are identical. Check it before the name re-check / insert.
                if (companies.MatchByCik(profile.Cik) is { } byCik) return byCik.Id;
                // sonar's name may differ from FMP's canonical one â€” re-check before inserting.
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
                catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* financials unavailable â€” company still created */ }
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
            Type = string.IsNullOrWhiteSpace(req.Ticker) ? CompanyType.PRIVATE : CompanyType.PUBLIC
        };
        // Name-only attempt: on a hit, roll sub -> industry -> sector up (self-heals the guessed sector);
        // on a miss leave it Pending (don't flag NoFit) so a later backfill can fetch a label and retry.
        ApplyClassification(stub, stubSub, markNoFitOnMiss: false);
        companies.Add(stub);
        return stub.Id;
    }

    public void ApplyFetchedData(Company e, CompanyCreateModel m) => CompanyMapper.Apply(e, m);

    public async Task<VolumeIngestResult> IngestWeeklyVolumeAsync(long companyId)
    {
        var company = companies.GetById(companyId);
        if (company is null) return new VolumeIngestResult(VolumeIngestStatus.CompanyNotFound, 0);

        // Yahoo keys off the ticker; we only have a CIK, so resolve it via the SEC map (US filers only).
        var ticker = ResolveTickerForCik(company.Cik, await stock.GetCikTickerMap());
        if (ticker is null) return new VolumeIngestResult(VolumeIngestStatus.NoTicker, 0);

        var bars = await yahoo.GetWeeklyVolumeHistoryAsync(ticker);
        if (bars is not { Count: > 0 }) return new VolumeIngestResult(VolumeIngestStatus.NoData, 0);

        var captured = DateTime.UtcNow;
        var rows = bars
            .Select(b => new CompanyVolumeHistory(companyId, b.WeekStart, b.Volume) { CapturedAt = captured })
            .ToList();
        companies.ReplaceVolumeHistory(companyId, rows);
        return new VolumeIngestResult(VolumeIngestStatus.Ok, rows.Count);
    }

    // FMP sometimes returns a null cik even for US filers (e.g. WEN -> SEC CIK 0000030697), which
    // would strand the company without a CIK and break every CIK-keyed feature (financials, weekly
    // volume). Fall back to the SEC ticker->CIK map for the missing ones.
    private async Task<string?> ResolveMissingCikAsync(string? cik, string ticker)
    {
        if (!string.IsNullOrWhiteSpace(cik)) return cik;
        try { return await stock.ResolveCik(ticker); }
        catch (HttpRequestException) { return cik; }   // SEC map unreachable -> leave unresolved
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

    // Apply a classifier result to a company. A hit sets the sub-industry and rolls Industry + Sector up
    // from it â€” the sub-industry is authoritative, so this self-heals a wrong/missing source sector (e.g.
    // FMP's "Energy/Solar" -> the real IT/Utilities home). A miss marks the row NoFit so the automatic
    // sweep stops retrying it and it surfaces in the Unclassified report for manual / AI re-resolution.
    // Returns true on a hit. `markNoFitOnMiss` is false for a name-only attempt (no label yet) where a
    // miss just means "couldn't tell from the name" â€” leave it Pending so a later backfill can fetch a
    // label and retry, rather than freezing it out of the automatic sweep.
    private static bool ApplyClassification(Company c, GicsSubIndustry? sub, bool markNoFitOnMiss = true)
    {
        if (sub is { } resolved)
        {
            c.GicsSubIndustry = resolved;
            c.Industry = resolved.GetIndustry();
            c.Sector = resolved.GetSector();
            c.ClassifyStatus = ClassifyStatus.Resolved;
            return true;
        }
        if (markNoFitOnMiss) c.ClassifyStatus = ClassifyStatus.NoFit;
        return false;
    }

    /// <inheritdoc/>
    public async Task<GicsSubIndustry?> ReclassifyAsync(long companyId, CancellationToken ct = default)
    {
        var c = companies.GetById(companyId);
        if (c is null) return null;

        // A human-pinned classification is never re-resolved â€” that's the whole point of the lock. Return
        // the current value so the caller reflects "still set" rather than treating it as a miss.
        if (c.ClassificationLocked) return c.GicsSubIndustry;

        // On-demand re-resolve: fetch the FMP label if we don't have one yet (so a later run isn't
        // name-only), then run the full constrained -> unconstrained classify. Always reattempts, even a
        // prior NoFit â€” this is exactly the "refill the wrong ones" path the Unclassified page triggers.
        if (string.IsNullOrWhiteSpace(c.FmpIndustry) && ResolveTickerForCik(c.Cik, await stock.GetCikTickerMap()) is { } t)
        {
            try
            {
                if (await fmp.GetProfileAsync(t) is { Industry: { } lbl } && !string.IsNullOrWhiteSpace(lbl))
                    c.FmpIndustry = lbl.Trim();
            }
            catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { /* classify by name */ }
        }

        // bypassCache: the button's whole job is to re-reason, not echo a cached/ambiguous mapping.
        var sub = await industryClassifier.ResolveSubIndustryAsync(
            c.Sector, c.FmpIndustry, c.Name, c.Notes, bypassCache: true, ct: ct);
        ApplyClassification(c, sub);
        companies.Update(c);
        return sub;
    }
}
