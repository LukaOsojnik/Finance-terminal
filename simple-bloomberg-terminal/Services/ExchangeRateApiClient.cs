using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for ExchangeRate-API's free open endpoint. Covers ~160 currencies (incl.
/// the pegged/exotic ones ECB omits), so it backs up Frankfurter for revenue conversion.
/// </summary>
public class ExchangeRateApiClient : IExchangeRateApiClient
{
    private readonly HttpClient _http;

    public ExchangeRateApiClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<double?> GetUsdRateAsync(string currency)
    {
        if (string.IsNullOrWhiteSpace(currency)) return null;
        if (string.Equals(currency, "USD", StringComparison.OrdinalIgnoreCase)) return 1.0;

        var resp = await _http.GetAsync($"/v6/latest/{Uri.EscapeDataString(currency)}");
        if (!resp.IsSuccessStatusCode) return null;
        var data = await resp.Content.ReadFromJsonAsync<ErApiLatest>();
        if (data?.Result != "success" || data.Rates == null) return null;
        return data.Rates.TryGetValue("USD", out var rate) ? rate : null;
    }
}

public record ErApiLatest(string? Result, Dictionary<string, double>? Rates);
