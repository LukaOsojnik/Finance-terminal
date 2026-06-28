using System.Net;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Financials;

/// <inheritdoc cref="ICompanyFinancialsService"/>
public class CompanyFinancialsService : ICompanyFinancialsService
{
    private readonly IFmpApiClient _fmp;
    private readonly IYahooFinanceClient _yahoo;
    private readonly ILogger<CompanyFinancialsService> _log;

    public CompanyFinancialsService(IFmpApiClient fmp, IYahooFinanceClient yahoo,
        ILogger<CompanyFinancialsService> log)
    {
        _fmp = fmp;
        _yahoo = yahoo;
        _log = log;
    }

    // The free tier caps statement `limit` at 5, so request 5 for both granularities.
    private const int PeriodLimit = 5;

    public async Task<IReadOnlyList<CompanyFinancial>> BuildAsync(long companyId, string symbol)
    {
        symbol = symbol.Trim();
        var captured = DateTime.UtcNow;

        // Each granularity is independently best-effort: a premium-gated or unreachable call for one
        // period (e.g. 402) must not discard rows already fetched for the other. Only when FMP yields
        // nothing at all (non-US, fully gated) do we fall back to Yahoo's annual income.
        // A 429 (daily quota) is NOT swallowed anywhere — it must bubble so callers (e.g. the bulk
        // backfill) stop instead of silently degrading to inferior Yahoo data right at the quota edge.
        var rows = new List<CompanyFinancial>();
        foreach (var period in new[] { "annual", "quarter" })
        {
            try { rows.AddRange(await FetchFmpPeriodAsync(companyId, symbol, period, PeriodLimit, captured)); }
            catch (HttpRequestException ex) when (ex.StatusCode != HttpStatusCode.TooManyRequests)
            { /* this period gated/unreachable (e.g. 402) — keep whatever else we have */
                _log.LogInformation("FMP {Period} unavailable for {Symbol}: {Reason}", period, symbol, Describe(ex.StatusCode));
            }
        }
        if (rows.Count > 0) return rows;

        _log.LogInformation("FMP returned no rows for {Symbol}; falling back to Yahoo annual income.", symbol);
        return await FetchYahooFallbackAsync(companyId, symbol, captured);
    }

    // Merge FMP's four statements for one granularity into one row per (FiscalYear, Period).
    // Income drives the row set; ratios/balance/cash-flow are matched in by the same key (their
    // endpoints may individually return null/empty, leaving those fields null).
    private async Task<List<CompanyFinancial>> FetchFmpPeriodAsync(
        long companyId, string symbol, string period, int limit, DateTime captured)
    {
        // Income drives the row set; if it's gated/unreachable, let it bubble so BuildAsync's
        // per-period catch can skip this granularity (and ultimately fall back to Yahoo).
        var income = await _fmp.GetIncomeStatementsAsync(symbol, period, limit);
        if (income is not { Count: > 0 }) return [];

        // Ratios/balance/cash-flow are secondary enrichment and can be independently plan-gated (e.g.
        // FMP's free tier gates QUARTERLY ratios with a 402). Treat each as best-effort: a failure
        // leaves those fields null — gross/op/net margins then derive from the income line items.
        var ratios = Index(await TryList(() => _fmp.GetRatiosAsync(symbol, period, limit)), r => (r.FiscalYear, r.Period));
        var balance = Index(await TryList(() => _fmp.GetBalanceSheetsAsync(symbol, period, limit)), b => (b.FiscalYear, b.Period));
        var cash = Index(await TryList(() => _fmp.GetCashFlowsAsync(symbol, period, limit)), c => (c.FiscalYear, c.Period));

        var rows = new List<CompanyFinancial>();
        foreach (var inc in income)
        {
            if (!TryParseYear(inc.FiscalYear, out var year) || ParsePeriod(inc.Period) is not { } fp) continue;

            var key = (inc.FiscalYear, inc.Period);
            var r = ratios.GetValueOrDefault(key);
            var b = balance.GetValueOrDefault(key);
            var c = cash.GetValueOrDefault(key);

            rows.Add(new CompanyFinancial(companyId, year, fp)
            {
                EndDate = ParseDate(inc.Date),
                ReportedCurrency = inc.ReportedCurrency,
                Source = DataSource.FMP,
                CapturedAt = captured,
                Revenue = inc.Revenue,
                CostOfRevenue = inc.CostOfRevenue,
                GrossProfit = inc.GrossProfit,
                OperatingIncome = inc.OperatingIncome,
                Ebitda = inc.Ebitda,
                NetIncome = inc.NetIncome,
                Eps = inc.Eps,
                // Prefer FMP's ready-made margins; derive gross margin from line items if absent.
                GrossMargin = r?.GrossProfitMargin ?? Ratio(inc.GrossProfit, inc.Revenue),
                OperatingMargin = r?.OperatingProfitMargin ?? Ratio(inc.OperatingIncome, inc.Revenue),
                NetMargin = r?.NetProfitMargin ?? Ratio(inc.NetIncome, inc.Revenue),
                CurrentRatio = r?.CurrentRatio,
                DebtToEquity = r?.DebtToEquityRatio,
                TotalCash = b?.CashAndShortTermInvestments,
                TotalDebt = b?.TotalDebt,
                OperatingCashFlow = c?.OperatingCashFlow,
                FreeCashFlow = c?.FreeCashFlow
            });
        }
        return rows;
    }

    // Non-US fallback: Yahoo's annual income history, which reliably carries only revenue + net income.
    private async Task<List<CompanyFinancial>> FetchYahooFallbackAsync(long companyId, string symbol, DateTime captured)
    {
        var annual = await _yahoo.GetAnnualIncomeAsync(symbol);
        if (annual is not { Count: > 0 }) return [];

        var rows = new List<CompanyFinancial>();
        foreach (var a in annual)
        {
            if (a.EndDate is not { } end) continue;
            rows.Add(new CompanyFinancial(companyId, end.Year, FiscalPeriod.FY)
            {
                EndDate = end,
                Source = DataSource.YAHOO,
                CapturedAt = captured,
                Revenue = a.Revenue,
                NetIncome = a.NetIncome,
                NetMargin = Ratio(a.NetIncome, a.Revenue)
            });
        }
        return rows;
    }

    // Build a lookup keyed by (fiscalYear, period) so each statement matches its income row. Skips
    // entries with a duplicate or null key (FMP shouldn't return dupes, but be defensive).
    private static Dictionary<(string?, string?), T> Index<T>(List<T>? list, Func<T, (string?, string?)> key)
    {
        var map = new Dictionary<(string?, string?), T>();
        if (list == null) return map;
        foreach (var item in list)
            map.TryAdd(key(item), item);
        return map;
    }

    // Best-effort fetch for a secondary statement: a premium-gated/unreachable call (e.g. quarterly
    // ratios -> 402) becomes null instead of throwing away the whole period's income-driven rows.
    private static async Task<List<T>?> TryList<T>(Func<Task<List<T>?>> fetch)
    {
        // Swallow a gated/unreachable secondary statement, but let a 429 (daily quota) bubble.
        try { return await fetch(); }
        catch (HttpRequestException ex) when (ex.StatusCode != HttpStatusCode.TooManyRequests) { return null; }
    }

    // Turn an FMP failure status into a short, human-readable reason for the log line (NOT the raw
    // number) so a backfill run reads "premium-gated" / "key invalid" instead of "402" / "401".
    private static string Describe(HttpStatusCode? status) => status switch
    {
        HttpStatusCode.PaymentRequired => "premium-gated (symbol not in this FMP plan)",
        HttpStatusCode.Unauthorized => "FMP key invalid or expired",
        HttpStatusCode.Forbidden => "FMP access forbidden",
        null => "no response (network/timeout)",
        _ => $"FMP request failed ({status})"
    };

    private static double? Ratio(double? num, double? den) =>
        num is { } n && den is { } d && d != 0 ? n / d : null;

    private static bool TryParseYear(string? s, out int year) =>
        int.TryParse(s, out year);

    private static FiscalPeriod? ParsePeriod(string? p) =>
        Enum.TryParse<FiscalPeriod>(p, ignoreCase: true, out var fp) ? fp : null;

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;
}
