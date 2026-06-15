using System.Text.Json;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="IIndustryClassifier"/>
public class IndustryClassifier : IIndustryClassifier
{
    private readonly IChatLlm _llm;

    public IndustryClassifier(IChatLlm llm)
    {
        _llm = llm;
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

        // The user's chosen parsing model may be a reasoning model that spends tokens BEFORE the answer
        // (reasoning alone runs ~65-122 tokens here). A tight budget (e.g. 100) gets cut off mid-reasoning
        // -> empty/truncated content -> JSON parse fails -> null. 400 leaves headroom for the reasoning
        // plus the one-line JSON answer so the reply finishes on "stop", not "length".
        // Industry is a best-effort enrichment (callers leave it null on a miss), so treat a user
        // without a DeepSeek key the same as DeepSeek being unreachable — return null, don't block the
        // create/link/backfill flow that called us. The explicit AI buttons surface the key popup.
        string raw;
        try { raw = await _llm.CompleteAsync(system, user, maxTokens: 400, jsonObject: true, ct: ct); }
        catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { return null; }

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
