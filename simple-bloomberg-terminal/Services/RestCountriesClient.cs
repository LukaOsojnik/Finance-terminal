using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for REST Countries (restcountries.com). The single-code /alpha/{code}
/// endpoint with a fields filter returns one JSON object (not an array).
/// </summary>
public class RestCountriesClient : IRestCountriesClient
{
    private readonly HttpClient _http;

    public RestCountriesClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://restcountries.com");
    }

    public async Task<RestCountry?> GetByCodeAsync(string code)
    {
        var resp = await _http.GetAsync(
            $"/v3.1/alpha/{Uri.EscapeDataString(code)}?fields=name,currencies,cca2,cca3,region,population");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<RestCountry>();
    }
}
