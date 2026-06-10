using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for Financial Modeling Prep. Base URL and API key come from the "Fmp"
/// config section. Both endpoints return a JSON array; we take the first element. A missing
/// symbol returns 404 or an empty array -> null.
/// </summary>
public class FmpApiClient : IFmpApiClient
{
    private readonly HttpClient _http;
    private readonly string _apiKey;

    public FmpApiClient(HttpClient http, IConfiguration config)
    {
        _http = http;
        _http.BaseAddress = new Uri(config["Fmp:BaseUrl"] ?? "https://financialmodelingprep.com");
        _apiKey = config["Fmp:ApiKey"] ?? "";
    }

    public async Task<FmpProfile?> GetProfileAsync(string symbol)
    {
        var list = await GetArray<FmpProfile>(
            $"/stable/profile?symbol={Uri.EscapeDataString(symbol)}&apikey={_apiKey}");
        return list?.FirstOrDefault();
    }

    public async Task<FmpIncome?> GetLatestIncomeAsync(string symbol)
    {
        var list = await GetArray<FmpIncome>(
            $"/stable/income-statement?symbol={Uri.EscapeDataString(symbol)}&limit=1&apikey={_apiKey}");
        return list?.FirstOrDefault();
    }

    public Task<List<FmpIncome>?> GetIncomeStatementsAsync(string symbol, string period, int limit) =>
        GetArray<FmpIncome>(Statement("income-statement", symbol, period, limit));

    public Task<List<FmpRatio>?> GetRatiosAsync(string symbol, string period, int limit) =>
        GetArray<FmpRatio>(Statement("ratios", symbol, period, limit));

    public Task<List<FmpBalance>?> GetBalanceSheetsAsync(string symbol, string period, int limit) =>
        GetArray<FmpBalance>(Statement("balance-sheet-statement", symbol, period, limit));

    public Task<List<FmpCashFlow>?> GetCashFlowsAsync(string symbol, string period, int limit) =>
        GetArray<FmpCashFlow>(Statement("cash-flow-statement", symbol, period, limit));

    private string Statement(string endpoint, string symbol, string period, int limit) =>
        $"/stable/{endpoint}?symbol={Uri.EscapeDataString(symbol)}&period={period}&limit={limit}&apikey={_apiKey}";

    private async Task<List<T>?> GetArray<T>(string url)
    {
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<List<T>>();
    }
}
