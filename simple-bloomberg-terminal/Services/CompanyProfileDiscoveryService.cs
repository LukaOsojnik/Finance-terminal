using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="ICompanyProfileDiscovery"/>
/// <remarks>
/// Reuses the Perplexity wire shape (<see cref="PerplexityRequest"/>) and by-hand envelope parse
/// from <see cref="CounterpartyDiscoveryService"/> — same "Perplexity" config section (base URL,
/// key, model). One grounded sonar call returns the whole profile as JSON; no planner/fan-out
/// (a single company needs one search, not several).
/// </remarks>
public class CompanyProfileDiscoveryService : ICompanyProfileDiscovery
{
    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;
    private readonly string _model;

    public CompanyProfileDiscoveryService(HttpClient http, IConfiguration config, IUserApiKeyProvider keys)
    {
        _http = http;
        _keys = keys;
        var section = config.GetSection("Perplexity");
        _http.BaseAddress = new Uri(section["BaseUrl"] ?? "https://api.perplexity.ai");
        _http.Timeout = TimeSpan.FromSeconds(90);
        _model = section["Model"] ?? "sonar-pro";
    }

    // The user's Perplexity key, or throw the "add your key" signal the front-end turns into a popup.
    private async Task<string> KeyAsync(CancellationToken ct)
    {
        var k = (await _keys.GetAsync(ct)).Perplexity;
        if (string.IsNullOrWhiteSpace(k)) throw MissingApiKeyException.Perplexity();
        return k;
    }

    public async Task<CompanyProfileResult?> DiscoverAsync(string companyName, CancellationToken ct = default)
    {
        var system =
            "You research a single company from current web sources and return its profile as JSON only " +
            "(no prose, no code fences). Fields: name (the company's common name), sector (EXACTLY one of " +
            "ENERGY, MATERIALS, INDUSTRIALS, CONSUMER_DISCRETIONARY, CONSUMER_STAPLES, HEALTH_CARE, " +
            "FINANCIALS, INFORMATION_TECHNOLOGY, COMMUNICATION_SERVICES, UTILITIES, REAL_ESTATE), industry " +
            "(its specific industry as a short label, e.g. 'Software', 'Semiconductors', 'Apparel " +
            "Manufacturing'), country_code (ISO-2 of its headquarters, e.g. US, DE), description (one or two " +
            "sentences on what it does), revenue_usd (the company's MOST RECENT yearly revenue in US dollars as " +
            "a plain number — no symbols/commas, e.g. 12000000000. Always pick the NEWEST year for which a " +
            "credible figure exists; if several years are reported, choose the latest one, including the most " +
            "recent full year or a trailing-12-month/annualized figure. Private companies rarely file official " +
            "numbers, so use the best credibly-reported figure or estimate from reputable financial press; do " +
            "NOT give a forward projection. Use null ONLY if there is no credible basis at all), revenue_year " +
            "(the year revenue_usd refers to, e.g. 2025; null if unknown), gross_margin (the company's ACTUAL " +
            "gross margin as a decimal 0-1, grounded in its real economics; null if it cannot be reasonably " +
            "grounded — do NOT output a generic industry-average guess), valuation_usd (the company's latest " +
            "VALUATION in US dollars as a plain number — for a private company the most recent post-money " +
            "valuation from a funding round or credible report; for a public company its market capitalization; " +
            "null if unknown). Reply: {\"name\":\"\",\"sector\":\"\",\"industry\":\"\",\"country_code\":null," +
            "\"description\":null,\"revenue_usd\":null,\"revenue_year\":null,\"gross_margin\":null," +
            "\"valuation_usd\":null}.";
        // Anchor "most recent" to today so the model returns the latest year, not a stale one it happens
        // to surface first (a fast-growing private's revenue varies wildly by year across sources).
        var user = $"Company: {companyName}. Today is {DateTime.UtcNow:MMMM yyyy}; report the most recent year's revenue available.";

        var req = new PerplexityRequest(
            Model: _model,
            Messages: [new DeepSeekMessage("system", system), new DeepSeekMessage("user", user)],
            MaxTokens: 1200,
            // "high" pulls more (primary) source content before answering — slower but the figures are
            // better grounded, which matters here since the result is saved, not just reviewed.
            WebSearchOptions: new PerplexityWebSearchOptions("high"));

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

        // sonar lists the web pages it used in a top-level `citations` array (the [n] markers in the
        // prose index into it). Surface them so the user can verify the figures' provenance.
        var sources = root.TryGetProperty("citations", out var cit) && cit.ValueKind == JsonValueKind.Array
            ? cit.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList()
            : [];

        return Parse(answer, sources);
    }

    // Slice the JSON object out of sonar's answer (tolerant of fences/prose), same trick as
    // CounterpartyDiscoveryService.Parse. Returns null if nothing parseable / no name.
    private static CompanyProfileResult? Parse(string answer, IReadOnlyList<string> sources)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(answer[start..(end + 1)]); }
        catch (JsonException) { return null; }

        using (doc)
        {
            var el = doc.RootElement;
            var name = Str(el, "name");
            if (string.IsNullOrWhiteSpace(name)) return null;
            return new CompanyProfileResult(
                Name: name,
                Sector: Str(el, "sector"),
                Industry: Str(el, "industry"),
                CountryCode: Str(el, "country_code"),
                Description: Str(el, "description"),
                RevenueUsd: Num(el, "revenue_usd"),
                GrossMargin: Num(el, "gross_margin"),
                RevenueYear: Num(el, "revenue_year") is { } y && y is >= 1900 and <= 2100 ? (int)y : null,
                ValuationUsd: Num(el, "valuation_usd"),
                Sources: sources);
        }
    }

    private static string? Str(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase) ? null : s;
    }

    // sonar may emit numbers as JSON numbers or numeric strings (sometimes with $/commas); strip and parse.
    private static double? Num(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("$", "").Replace(",", "").Trim();
        return double.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
