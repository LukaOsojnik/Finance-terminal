using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

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
    private string? _crumb;

    public YahooFinanceClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://query1.finance.yahoo.com");
        // Yahoo rejects the default .NET User-Agent; a browser-like UA is required.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
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
                if (resp.StatusCode == HttpStatusCode.Unauthorized) { _crumb = null; continue; }
                if (!resp.IsSuccessStatusCode) return null;

                return await resp.Content.ReadFromJsonAsync<T>();
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException or TaskCanceledException)
        {
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
