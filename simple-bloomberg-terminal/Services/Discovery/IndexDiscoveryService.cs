using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Discovery;

/// <inheritdoc cref="IIndexDiscovery"/>
/// <remarks>
/// Mirrors <see cref="CompanyProfileDiscoveryService"/>: same "Perplexity" config section + by-hand
/// envelope parse, one grounded sonar call (a query needs one search, not a fan-out). The critical
/// field is wikipedia_page â€” the import pipeline scrapes Wikipedia, so each suggestion must carry the
/// constituents-table page path or it can't be fetched.
/// </remarks>
public class IndexDiscoveryService : IIndexDiscovery
{
    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;

    public IndexDiscoveryService(HttpClient http, IUserApiKeyProvider keys)
    {
        _http = http;
        _keys = keys;
    }

    private Task<string> KeyAsync(CancellationToken ct) =>
        _keys.RequireAsync(k => k.Perplexity, MissingApiKeyException.Perplexity, ct);

    public async Task<IReadOnlyList<DiscoveredIndex>> DiscoverAsync(string query, CancellationToken ct = default)
    {
        var system =
            "You identify stock-market indices matching a user request and return them as JSON only (no " +
            "prose, no code fences). For EACH index provide: code (a short lowercase slug, e.g. 'nasdaq100', " +
            "'sp500', 'ftse100'), name (the index's common name), wikipedia_page (the path on ENGLISH " +
            "Wikipedia of the page that contains the index's CONSTITUENTS / COMPONENTS table â€” the list of " +
            "member companies â€” e.g. '/wiki/Nasdaq-100', '/wiki/List_of_S%26P_500_companies', " +
            "'/wiki/FTSE_100_Index'. Give the path starting with /wiki/, NOT a full URL. Only include an " +
            "index if such a constituents page exists), region (a short label like 'US', 'UK', 'Europe'; " +
            "null if unclear), sector (if the index covers ONE sector, the GICS sector as one of: ENERGY, " +
            "MATERIALS, INDUSTRIALS, CONSUMER_DISCRETIONARY, CONSUMER_STAPLES, HEALTH_CARE, FINANCIALS, " +
            "INFORMATION_TECHNOLOGY, COMMUNICATION_SERVICES, UTILITIES, REAL_ESTATE; if it is a broad " +
            "multi-sector index like the S&P 500 or FTSE 100, use null), etf_ticker (the ticker of the " +
            "State Street SPDR ETF that tracks this index if one exists â€” e.g. 'SPY' for S&P 500, 'DIA' " +
            "for the Dow, 'XLK' for US technology, 'XLF' for US financials; null if no SPDR ETF tracks it). " +
            "Return at most 12, most relevant first. Reply: {\"indices\":[{\"code\":\"\",\"name\":\"\"," +
            "\"wikipedia_page\":\"\",\"region\":null,\"sector\":null,\"etf_ticker\":null}]}. " +
            "If you find none, reply {\"indices\":[]}.";
        var user = $"Request: {query}";

        var model = (await _keys.GetAsync(ct)).WebSearchModel ?? ChatProviders.DefaultWebSearchModel;
        var req = new PerplexityRequest(
            Model: model,
            Messages: [new DeepSeekMessage("system", system), new DeepSeekMessage("user", user)],
            MaxTokens: 1500,
            WebSearchOptions: new PerplexityWebSearchOptions("medium"));

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(req),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();

        using var env = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = env.RootElement;
        var answer = root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content)
            ? content.GetString() ?? "" : "";

        return Parse(answer);
    }

    // Pull {"indices":[...]} out of sonar's answer (salvage a length-truncated array with "]}"). Drop any
    // suggestion whose wikipedia_page isn't a relative /wiki/ path; normalize a full URL down to its path.
    private static IReadOnlyList<DiscoveredIndex> Parse(string answer)
    {
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null || !doc.RootElement.TryGetProperty("indices", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return [];

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<DiscoveredIndex>();
        foreach (var el in arr.EnumerateArray())
        {
            var code = LlmJson.Str(el, "code");
            var name = LlmJson.Str(el, "name");
            var page = NormalizeWikiPage(LlmJson.Str(el, "wikipedia_page"));
            if (code is null || name is null || page is null) continue;
            if (!seen.Add(code)) continue;   // one row per slug
            list.Add(new DiscoveredIndex(
                code, name, page, LlmJson.Str(el, "region"),
                MapSector(LlmJson.Str(el, "sector")), NormalizeTicker(LlmJson.Str(el, "etf_ticker"))));
        }
        return list;
    }

    // Map sonar's free-text sector to the GICS Sector enum; anything it can't match (incl. "broad"
    // or null) becomes null = a broad-market index, which the catalog groups under "Broad Market".
    private static Sector? MapSector(string? raw) =>
        Enum.TryParse<Sector>(raw, ignoreCase: true, out var s) ? s : null;

    // Keep an ETF ticker only if it's a plausible symbol (letters, â‰¤6 chars); drop noise like "none".
    private static string? NormalizeTicker(string? raw)
    {
        var t = raw?.Trim().ToUpperInvariant();
        return !string.IsNullOrEmpty(t) && t.Length <= 6 && t.All(char.IsLetter) ? t : null;
    }

    // Accept "/wiki/Foo" as-is; reduce "https://en.wikipedia.org/wiki/Foo" to "/wiki/Foo"; reject
    // anything else (wrong host, non-article path) so a bad suggestion can't reach the scraper.
    private static string? NormalizeWikiPage(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var s = raw.Trim();
        if (s.StartsWith("/wiki/", StringComparison.Ordinal)) return s;
        if (Uri.TryCreate(s, UriKind.Absolute, out var uri)
            && uri.Host.EndsWith("wikipedia.org", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.StartsWith("/wiki/", StringComparison.Ordinal))
            return uri.AbsolutePath;
        return null;
    }
}
