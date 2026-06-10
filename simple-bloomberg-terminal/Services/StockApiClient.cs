using System.Net;
using System.Net.Http.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for SEC EDGAR. Base address is data.sec.gov; the ticker map lives on
/// www.sec.gov so that one call uses an absolute URL. SEC blocks requests without a
/// User-Agent carrying a contact email, so it is set on the injected client.
/// </summary>
public class StockApiClient : IStockApiClient
{
    private const string TickerMapUrl = "https://www.sec.gov/files/company_tickers.json";

    private readonly HttpClient _http;

    public StockApiClient(HttpClient http)
    {
        _http = http;
        _http.BaseAddress = new Uri("https://data.sec.gov");
        // SEC requires an identifying User-Agent (contact email) or it returns 403. The email's
        // '@' makes the string an invalid structured User-Agent, so skip header validation.
        if (!_http.DefaultRequestHeaders.Contains("User-Agent"))
            _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "simple-bloomberg-terminal lukaosojnikinfo@gmail.com");
    }

    public async Task<EdgarCompanyFacts?> GetCompanyFacts(string cik10)
    {
        var resp = await _http.GetAsync($"/api/xbrl/companyfacts/CIK{cik10}.json");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EdgarCompanyFacts>();
    }

    public async Task<EdgarSubmissions?> GetSubmissions(string cik10)
    {
        var resp = await _http.GetAsync($"/submissions/CIK{cik10}.json");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadFromJsonAsync<EdgarSubmissions>();
    }

    public async Task<string?> ResolveCik(string ticker)
    {
        var map = await _http.GetFromJsonAsync<Dictionary<string, EdgarTicker>>(TickerMapUrl);
        var match = map?.Values.FirstOrDefault(t =>
            string.Equals(t.Ticker, ticker, StringComparison.OrdinalIgnoreCase));
        return match is null ? null : match.CikStr.ToString().PadLeft(10, '0');
    }

    public async Task<IReadOnlyDictionary<string, string>> GetCikTickerMap()
    {
        var map = await _http.GetFromJsonAsync<Dictionary<string, EdgarTicker>>(TickerMapUrl);
        var byCik = new Dictionary<string, string>();
        if (map != null)
            foreach (var t in map.Values)
                byCik.TryAdd(t.CikStr.ToString().PadLeft(10, '0'), t.Ticker);  // first share class wins
        return byCik;
    }

    public async Task<string?> GetCompanyFactsJson(string cik10)
    {
        var resp = await _http.GetAsync($"/api/xbrl/companyfacts/CIK{cik10}.json");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }

    public async Task<string?> GetFilingDocument(string cik, string accessionNoDashes, string primaryDocument)
    {
        // Filing documents live under the Archives tree on www.sec.gov (absolute URL).
        var url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{accessionNoDashes}/{primaryDocument}";
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        resp.EnsureSuccessStatusCode();
        return await resp.Content.ReadAsStringAsync();
    }
}
