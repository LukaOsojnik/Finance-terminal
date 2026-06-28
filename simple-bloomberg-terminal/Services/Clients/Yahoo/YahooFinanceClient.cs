using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace simple_bloomberg_terminal.Services.Clients.Yahoo;

/// <summary>
/// Typed HttpClient for Yahoo Finance's unofficial quoteSummary API. Yahoo requires a session
/// cookie plus a matching "crumb" token on each data call, so we seed a cookie (fc.yahoo.com),
/// fetch a crumb, then query. A stale crumb returns 401 — we drop it and re-handshake once.
/// Everything is best-effort: any failure returns null so the caller falls back to manual entry.
/// </summary>
public class YahooFinanceClient : IYahooFinanceClient
{
    private const string CookieSeedUrl = "https://fc.yahoo.com";
    private const string CrumbUrl = "https://query1.finance.yahoo.com/v1/test/getcrumb";

    private readonly HttpClient _http;
    private readonly ILogger<YahooFinanceClient> _logger;
    private string? _crumb;

    public YahooFinanceClient(HttpClient http, ILogger<YahooFinanceClient> logger)
    {
        _http = http;
        _logger = logger;
    }

    public async Task<YahooFinancials?> GetFinancialsAsync(string symbol)
    {
        var fd = (await QuerySummaryAsync<YahooEnvelope>(symbol, "financialData"))
            ?.QuoteSummary?.Result?.FirstOrDefault()?.FinancialData;
        return fd == null ? null : new YahooFinancials(fd.TotalRevenue?.Raw, fd.GrossMargins?.Raw, fd.FinancialCurrency);
    }

    public async Task<IReadOnlyList<YahooAnnualIncome>?> GetAnnualIncomeAsync(string symbol)
    {
        var rows = (await QuerySummaryAsync<YahooIncomeEnvelope>(symbol, "incomeStatementHistory"))
            ?.QuoteSummary?.Result?.FirstOrDefault()?.IncomeStatementHistory?.IncomeStatementHistory;
        if (rows == null) return null;

        return rows.Select(r => new YahooAnnualIncome(
            r.EndDate?.Raw is { } raw ? DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(raw).UtcDateTime) : null,
            r.TotalRevenue?.Raw,
            r.NetIncome?.Raw)).ToList();
    }

    public async Task<IReadOnlyList<YahooWeeklyVolume>?> GetWeeklyVolumeHistoryAsync(string symbol)
    {
        try
        {
            // Chart endpoint is crumb-free, so no EnsureCrumb handshake here.
            // NB: range=max silently coerces interval to 3mo (quarterly) to cap point count. To get TRUE
            // weekly bars over full history we must pass an explicit window: period1=0 (epoch — Yahoo
            // clamps to the first trade date) .. period2=now. This returns ~1wk granularity (gap≈7d).
            var period2 = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var resp = await _http.GetAsync(
                $"/v8/finance/chart/{Uri.EscapeDataString(symbol)}?period1=0&period2={period2}&interval=1wk");
            if (!resp.IsSuccessStatusCode) return null;

            var result = (await resp.Content.ReadFromJsonAsync<YahooChartEnvelope>())
                ?.Chart?.Result?.FirstOrDefault();
            var timestamps = result?.Timestamp;
            var volumes = result?.Indicators?.Quote?.FirstOrDefault()?.Volume;
            if (timestamps == null || volumes == null) return null;

            var rows = new List<YahooWeeklyVolume>(timestamps.Count);
            for (var i = 0; i < timestamps.Count && i < volumes.Count; i++)
            {
                // Skip gap weeks (holidays/halts come back as null volume).
                if (volumes[i] is not { } vol) continue;
                var weekStart = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(timestamps[i]).UtcDateTime);
                rows.Add(new YahooWeeklyVolume(weekStart, vol));
            }
            return rows;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Yahoo weekly volume fetch failed for {Symbol}", symbol);
            return null;
        }
    }

    // Shared quoteSummary call: ensure a crumb, query the given modules, deserialize to T. Handles the
    // stale-crumb re-handshake (401 -> drop crumb, retry once) and swallows transport/parse failures to
    // null so callers fall back to manual entry. T is whichever module envelope the caller needs.
    private async Task<T?> QuerySummaryAsync<T>(string symbol, string modules) where T : class
    {
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var crumb = await EnsureCrumb();
                if (string.IsNullOrEmpty(crumb)) return null;

                var resp = await _http.GetAsync(
                    $"/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules={modules}&crumb={Uri.EscapeDataString(crumb)}");

                // Stale crumb -> clear it and re-handshake once.
                if (resp.StatusCode == HttpStatusCode.Unauthorized) { _logger.LogDebug("Yahoo stale crumb for {Symbol}, retrying", symbol); _crumb = null; continue; }
                if (!resp.IsSuccessStatusCode) return null;

                return await resp.Content.ReadFromJsonAsync<T>();
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Yahoo quoteSummary failed for {Symbol}/{Modules}", symbol, modules);
            return null;
        }
    }

    private async Task<string?> EnsureCrumb()
    {
        if (!string.IsNullOrEmpty(_crumb)) return _crumb;
        // Seeds the session cookie (this URL itself 404s, but sets the cookie we need).
        await _http.GetAsync(CookieSeedUrl);
        var crumb = await _http.GetStringAsync(CrumbUrl);
        _crumb = string.IsNullOrWhiteSpace(crumb) ? null : crumb;
        return _crumb;
    }
}
