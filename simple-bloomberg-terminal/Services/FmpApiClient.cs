using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for Financial Modeling Prep. Base URL comes from the "Fmp" config section; the
/// API key is the CURRENT USER's own key (bring-your-own), resolved per request from
/// <see cref="IUserApiKeyProvider"/> — no global key. A user without an FMP key triggers a
/// <see cref="MissingApiKeyException"/>. Both endpoints return a JSON array; we take the first
/// element. A missing symbol returns 404 or an empty array -> null.
/// </summary>
public class FmpApiClient : IFmpApiClient
{
    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;

    public FmpApiClient(HttpClient http, IConfiguration config, IUserApiKeyProvider keys)
    {
        _http = http;
        _keys = keys;
        _http.BaseAddress = new Uri(config["Fmp:BaseUrl"] ?? "https://financialmodelingprep.com");
    }

    // The user's FMP key, or throw the "add your key" signal the front-end turns into a popup.
    private async Task<string> KeyAsync()
    {
        var k = (await _keys.GetAsync()).Fmp;
        if (string.IsNullOrWhiteSpace(k)) throw MissingApiKeyException.Fmp();
        return k;
    }

    public async Task<FmpProfile?> GetProfileAsync(string symbol)
    {
        var key = await KeyAsync();
        var list = await GetArray<FmpProfile>(
            $"/stable/profile?symbol={Uri.EscapeDataString(symbol)}&apikey={key}");
        return list?.FirstOrDefault();
    }

    public async Task<FmpIncome?> GetLatestIncomeAsync(string symbol)
    {
        var key = await KeyAsync();
        var list = await GetArray<FmpIncome>(
            $"/stable/income-statement?symbol={Uri.EscapeDataString(symbol)}&limit=1&apikey={key}");
        return list?.FirstOrDefault();
    }

    public async Task<List<FmpIncome>?> GetIncomeStatementsAsync(string symbol, string period, int limit) =>
        await GetArray<FmpIncome>(await Statement("income-statement", symbol, period, limit));

    public async Task<List<FmpRatio>?> GetRatiosAsync(string symbol, string period, int limit) =>
        await GetArray<FmpRatio>(await Statement("ratios", symbol, period, limit));

    public async Task<List<FmpBalance>?> GetBalanceSheetsAsync(string symbol, string period, int limit) =>
        await GetArray<FmpBalance>(await Statement("balance-sheet-statement", symbol, period, limit));

    public async Task<List<FmpCashFlow>?> GetCashFlowsAsync(string symbol, string period, int limit) =>
        await GetArray<FmpCashFlow>(await Statement("cash-flow-statement", symbol, period, limit));

    private async Task<string> Statement(string endpoint, string symbol, string period, int limit) =>
        $"/stable/{endpoint}?symbol={Uri.EscapeDataString(symbol)}&period={period}&limit={limit}&apikey={await KeyAsync()}";

    private async Task<List<T>?> GetArray<T>(string url)
    {
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<T>>();
    }
}
