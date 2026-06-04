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
        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var crumb = await EnsureCrumb();
                if (string.IsNullOrEmpty(crumb)) return null;

                var resp = await _http.GetAsync(
                    $"/v10/finance/quoteSummary/{Uri.EscapeDataString(symbol)}?modules=financialData&crumb={Uri.EscapeDataString(crumb)}");

                // Stale crumb -> clear it and re-handshake once.
                if (resp.StatusCode == HttpStatusCode.Unauthorized) { _crumb = null; continue; }
                if (!resp.IsSuccessStatusCode) return null;

                var env = await resp.Content.ReadFromJsonAsync<YahooEnvelope>();
                var fd = env?.QuoteSummary?.Result?.FirstOrDefault()?.FinancialData;
                if (fd == null) return null;
                return new YahooFinancials(fd.TotalRevenue?.Raw, fd.GrossMargins?.Raw, fd.FinancialCurrency);
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
