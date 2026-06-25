using System.Net;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Indices;

public class IndexImportService(
    IWikipediaIndexClient wiki,
    ISpdrHoldingsClient spdr,
    IStockApiClient sec,
    IStockIndexRepository indices,
    ICompanyRepository companies,
    ICompanyProvisioningService provisioning) : IIndexImportService
{
    public async Task<IndexImportResult> ImportAsync(IndexImportRequest req, IProgress<string>? progress = null)
    {
        if (string.IsNullOrWhiteSpace(req.Code) && string.IsNullOrWhiteSpace(req.EtfTicker))
            throw new ArgumentException("Need an index code or a SPDR ETF ticker.", nameof(req));

        // SPDR is preferred whenever a ticker is known (real weights + free sector). If the ticker
        // isn't a SPDR fund (404) or the file is empty, fall back to Wikipedia when a page is available.
        if (!string.IsNullOrWhiteSpace(req.EtfTicker))
        {
            try { return await ImportFromSpdrAsync(req, progress); }
            catch (HttpRequestException) when (!string.IsNullOrWhiteSpace(req.WikiPage)) { }
            catch (InvalidOperationException) when (!string.IsNullOrWhiteSpace(req.WikiPage)) { }
        }

        return await ImportFromWikipediaAsync(req, progress);
    }

    // ── SPDR: real published weights from the daily-holdings file ──
    private async Task<IndexImportResult> ImportFromSpdrAsync(IndexImportRequest req, IProgress<string>? progress)
    {
        var ticker = req.EtfTicker!.Trim();
        progress?.Report($"Fetching SPDR holdings for {ticker.ToUpperInvariant()}…");
        var file = await spdr.GetHoldingsAsync(ticker);
        if (file.Holdings.Count == 0)
            throw new InvalidOperationException($"SPDR returned no holdings for '{ticker}'.");

        progress?.Report("Matching holdings to companies…");
        var tickerToCik = await TickerToCikAsync();
        var cikToId = companies.CikToIdMap();

        // First pass: link to existing companies (summing dual-class lines into one company's weight),
        // collect the holdings we couldn't match.
        var weightByCompany = new Dictionary<long, double?>();
        var unmatched = new List<SpdrHolding>();
        foreach (var h in file.Holdings)
        {
            if (TryResolve(h.Ticker, tickerToCik, cikToId, out var id)) AddWeight(weightByCompany, id, h.WeightPct);
            else unmatched.Add(h);
        }

        // Provision the missing members from FMP, then carry their file weight too.
        var (created, stoppedEarly) = await ProvisionMissingAsync(unmatched.Select(h => h.Ticker), progress);
        foreach (var h in unmatched)
            if (created.TryGetValue(h.Ticker, out var newId)) AddWeight(weightByCompany, newId, h.WeightPct);

        var rows = weightByCompany
            .Select(kv => new IndexConstituent { CompanyId = kv.Key, WeightPct = kv.Value is { } w ? Math.Round(w, 4) : null })
            .ToList();

        var code = string.IsNullOrWhiteSpace(req.Code) ? ticker.ToLowerInvariant() : req.Code;
        var name = FirstNonBlank(req.Name, file.FundName, ticker.ToUpperInvariant());
        var indexId = Persist(code, name, req.Sector, req.Region ?? "US", "SPDR", ticker.ToUpperInvariant(),
            rows, MatchedCap(rows));

        return new IndexImportResult(indexId, name, file.Holdings.Count, rows.Count, created.Count,
            created.Values.ToList(), Math.Round(rows.Sum(r => r.WeightPct ?? 0), 2), "SPDR", stoppedEarly);
    }

    // ── Wikipedia: scraped membership, cap-weighted from stored MarketCap ──
    private async Task<IndexImportResult> ImportFromWikipediaAsync(IndexImportRequest req, IProgress<string>? progress)
    {
        if (!IsWikiPath(req.WikiPage))
            throw new ArgumentException("wikiPage must be an English-Wikipedia /wiki/ path.", nameof(req));

        progress?.Report("Scraping Wikipedia constituents…");
        var constituents = await wiki.GetConstituentsAsync(req.WikiPage!);
        if (constituents.Count == 0)
            throw new InvalidOperationException($"Wikipedia returned no constituents for '{req.Code}'.");

        progress?.Report($"Matching {constituents.Count} members to companies…");
        var cikToId = companies.CikToIdMap();

        // Only pay for the SEC ticker map if some row lacks a CIK (NASDAQ-100/Dow tables omit it).
        IReadOnlyDictionary<string, string>? tickerToCik =
            constituents.Any(c => c.Cik is null) ? await TickerToCikAsync() : null;

        var matchedIds = new List<long>();
        var seen = new HashSet<long>();
        var unmatched = new List<string>();
        foreach (var c in constituents)
        {
            var cik = c.Cik;
            if (cik is null && tickerToCik != null && tickerToCik.TryGetValue(c.Ticker, out var resolved))
                cik = resolved;
            if (cik is not null && cikToId.TryGetValue(cik, out var companyId))
            {
                if (seen.Add(companyId)) matchedIds.Add(companyId);
            }
            else unmatched.Add(c.Ticker);
        }

        // Provision the missing members from FMP and fold their new ids into the matched set.
        var (created, stoppedEarly) = await ProvisionMissingAsync(unmatched, progress);
        foreach (var newId in created.Values)
            if (seen.Add(newId)) matchedIds.Add(newId);

        // Cap-weight from stored MarketCap: weight_i = cap_i / Σcap * 100. Members without a MarketCap
        // get a null weight and are excluded from the denominator (so the rest still sum sensibly).
        var caps = companies.MarketCapsByIds(matchedIds);
        var totalCap = caps.Values.Where(v => v is > 0).Sum(v => v!.Value);
        var rows = matchedIds.Select(id =>
        {
            double? weight = totalCap > 0 && caps.TryGetValue(id, out var cap) && cap is > 0
                ? Math.Round(cap!.Value / totalCap * 100, 4)
                : null;
            return new IndexConstituent { CompanyId = id, WeightPct = weight };
        }).ToList();

        var name = string.IsNullOrWhiteSpace(req.Name) ? req.Code : req.Name;
        var indexId = Persist(req.Code, name, req.Sector, req.Region, "Wikipedia", null, rows, totalCap);

        return new IndexImportResult(indexId, name, constituents.Count, rows.Count, created.Count,
            created.Values.ToList(), Math.Round(rows.Sum(r => r.WeightPct ?? 0), 2), "Wikipedia", stoppedEarly);
    }

    // Create companies for tickers not yet in the DB (so the whole index can be connected, not just the
    // members that happened to already exist). Returns ticker -> new company id for the ones created,
    // plus StoppedEarly: true when the loop bailed because FMP had no key or hit its daily limit (vs.
    // running to completion). StoppedEarly is the "continuable" signal — the still-missing members could
    // be linked by re-running under a key with remaining quota. An individual ticker that errors or has
    // no FMP profile (e.g. a non-US member) is skipped without flipping StoppedEarly.
    private async Task<(Dictionary<string, long> Created, bool StoppedEarly)> ProvisionMissingAsync(
        IEnumerable<string> tickers, IProgress<string>? progress)
    {
        var todo = tickers.Where(t => !string.IsNullOrWhiteSpace(t))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var created = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        if (todo.Count == 0) return (created, false);

        progress?.Report($"Adding {todo.Count} missing companies from FMP…");
        foreach (var t in todo)
        {
            try
            {
                if (await provisioning.CreateFromProfileAsync(t) is { } company) created[t] = company.Id;
            }
            catch (MissingApiKeyException) { return (created, true); }   // no FMP key -> continuable
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests) { return (created, true); }  // daily cap -> continuable
            catch (HttpRequestException) { }            // this ticker failed; try the rest
        }
        return (created, false);
    }

    private static bool TryResolve(string ticker, IReadOnlyDictionary<string, string> tickerToCik,
        IReadOnlyDictionary<string, long> cikToId, out long id)
    {
        id = 0;
        return tickerToCik.TryGetValue(ticker, out var cik) && cikToId.TryGetValue(cik, out id);
    }

    // Add a (possibly null) weight to a company's running total; preserves null until a real weight is seen.
    private static void AddWeight(Dictionary<long, double?> byCompany, long id, double? weight)
    {
        if (byCompany.TryGetValue(id, out var cur))
            byCompany[id] = weight is null ? cur : (cur ?? 0) + weight.Value;
        else
            byCompany[id] = weight;
    }

    // Upsert the index row (reuse on re-import so membership + classification refresh in place) and
    // replace its constituents. Shared by both sources. The sector is inferred from what the members
    // actually are (the source files carry no reliable index-level sector); `sectorHint` (e.g. from
    // Perplexity) is the fallback when no single sector dominates our matched members.
    private long Persist(string code, string name, Sector? sectorHint, string? region, string provider,
        string? etfProxy, IReadOnlyList<IndexConstituent> rows, double totalMarketCap)
    {
        var index = indices.GetByCode(code);
        if (index is null)
        {
            index = new StockIndex(name, code);
            indices.Add(index);
        }
        else
        {
            index.Name = name;
        }

        index.Sector = InferSectorFromMembers(rows) ?? sectorHint;
        index.Region = region;
        index.Provider = provider;
        index.EtfProxy = etfProxy;
        index.TotalMarketCap = totalMarketCap > 0 ? totalMarketCap : null;

        indices.ReplaceConstituents(index.Id, rows, DateOnly.FromDateTime(DateTime.UtcNow));
        return index.Id;
    }

    // Σ stored MarketCap of the matched members — the catalog's "size" metric, computed the same way
    // regardless of how the weights were sourced so SPDR and Wikipedia indices sort comparably.
    private double MatchedCap(IReadOnlyList<IndexConstituent> rows)
    {
        var caps = companies.MarketCapsByIds(rows.Select(r => r.CompanyId));
        return caps.Values.Where(v => v is > 0).Sum(v => v!.Value);
    }

    // ticker -> CIK, inverted from the SEC cik->ticker map (first ticker per cik wins).
    private async Task<IReadOnlyDictionary<string, string>> TickerToCikAsync()
    {
        var cikToTicker = await sec.GetCikTickerMap();
        var inverted = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (cik, ticker) in cikToTicker) inverted.TryAdd(ticker, cik);
        return inverted;
    }

    // Classify the index by what its matched members are: a fund whose members are ≥80% one sector (by
    // weight, or by count when weights are absent) is that sector (a Select Sector SPDR like XLK is
    // ~100% one sector); a broad index like the S&P 500 spreads across all -> null = broad market.
    private Sector? InferSectorFromMembers(IReadOnlyList<IndexConstituent> rows)
    {
        if (rows.Count == 0) return null;
        var sectors = companies.SectorsByIds(rows.Select(r => r.CompanyId));
        var useWeights = rows.Any(r => r.WeightPct is > 0);

        var bySector = new Dictionary<Sector, double>();
        double total = 0;
        foreach (var r in rows)
        {
            if (!sectors.TryGetValue(r.CompanyId, out var s)) continue;
            var contrib = useWeights ? r.WeightPct ?? 0 : 1;
            bySector[s] = bySector.GetValueOrDefault(s) + contrib;
            total += contrib;
        }
        if (total <= 0) return null;
        var top = bySector.OrderByDescending(kv => kv.Value).First();
        return top.Value / total >= 0.8 ? top.Key : null;
    }

    private static string FirstNonBlank(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim() ?? "";

    // Guard the client-supplied page: it must be a relative English-Wikipedia article path, so the
    // typed client (BaseAddress en.wikipedia.org) can't be steered to scrape an arbitrary host.
    private static bool IsWikiPath(string? page) =>
        !string.IsNullOrWhiteSpace(page) && page.StartsWith("/wiki/", StringComparison.Ordinal)
        && !page.Contains("://");
}
