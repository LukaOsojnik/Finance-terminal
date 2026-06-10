using System.Text.Json;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="IIndustryClassifier"/>
public class IndustryClassifier : IIndustryClassifier
{
    private readonly IDeepSeekClient _deepSeek;
    private readonly string _model;

    public IndustryClassifier(IDeepSeekClient deepSeek, IConfiguration config)
    {
        _deepSeek = deepSeek;
        _model = config["DeepSeek:ScanModel"] ?? "deepseek-v4-flash";
    }

    public async Task<GicsIndustry?> ClassifyAsync(Sector sector, string? companyName, string? sourceLabel, CancellationToken ct = default)
    {
        // Constrain the model to the industries that actually belong to this sector — so a stray pick
        // can't land in the wrong sector — and re-check the sector on the parsed result anyway.
        var candidates = Enum.GetValues<GicsIndustry>().Where(i => i.GetSector() == sector).ToList();
        if (candidates.Count == 0) return null;

        var allowed = string.Join(", ", candidates.Select(c => c.ToString()));
        var system = "You classify a company into exactly one GICS industry. Reply ONLY with JSON " +
            "{\"industry\":\"ENUM_NAME\"} where ENUM_NAME is exactly one of the allowed values, " +
            "or {\"industry\":null} if none fit.";
        var user = $"Company: {companyName}\nSector: {sector}\n" +
            $"Source industry label: {sourceLabel}\nAllowed industries: {allowed}";

        string raw;
        try { raw = await _deepSeek.CompleteAsync(_model, system, user, maxTokens: 100, jsonObject: true, ct); }
        catch (HttpRequestException) { return null; }

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (doc.RootElement.TryGetProperty("industry", out var el) &&
                el.ValueKind == JsonValueKind.String &&
                Enum.TryParse<GicsIndustry>(el.GetString(), out var parsed) &&
                parsed.GetSector() == sector)
                return parsed;
        }
        catch (JsonException) { /* malformed reply -> no industry */ }
        return null;
    }
}
