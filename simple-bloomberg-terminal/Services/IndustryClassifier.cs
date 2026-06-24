using System.Text.Json;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="IIndustryClassifier"/>
public class IndustryClassifier : IIndustryClassifier
{
    private readonly IChatLlm _llm;
    private readonly IFmpIndustryMappingRepository _mappings;
    private readonly ILogger<IndustryClassifier> _logger;

    public IndustryClassifier(IChatLlm llm, IFmpIndustryMappingRepository mappings, ILogger<IndustryClassifier> logger)
    {
        _llm = llm;
        _mappings = mappings;
        _logger = logger;
    }

    public async Task<GicsSubIndustry?> ResolveSubIndustryAsync(Sector sector, string? fmpLabel, string? companyName, CancellationToken ct = default)
    {
        // A vendor label maps to the same sub-industry for every company, so look it up in the learned
        // cache first — that's what keeps each distinct label to a single model call, ever.
        if (!string.IsNullOrWhiteSpace(fmpLabel) && _mappings.Get(fmpLabel) is { } cached)
            return cached;

        var sub = await ClassifyAsync(sector, companyName, fmpLabel, ct);

        // Persist the freshly-learned label -> sub-industry so the next company with this label is free.
        if (sub is { } resolved && !string.IsNullOrWhiteSpace(fmpLabel))
            _mappings.Set(fmpLabel, resolved);

        return sub;
    }

    private async Task<GicsSubIndustry?> ClassifyAsync(Sector sector, string? companyName, string? sourceLabel, CancellationToken ct)
    {
        // Constrain the model to the sub-industries that actually belong to this sector — so a stray pick
        // can't land in the wrong sector — and re-check the sector on the parsed result anyway.
        var candidates = Enum.GetValues<GicsSubIndustry>().Where(i => i.GetSector() == sector).ToList();
        if (candidates.Count == 0) return null;

        var allowed = string.Join(", ", candidates.Select(c => c.ToString()));
        var system = "You classify a company into exactly one GICS sub-industry. Reply ONLY with JSON " +
            "{\"subIndustry\":\"ENUM_NAME\"} where ENUM_NAME is exactly one of the allowed values, " +
            "or {\"subIndustry\":null} if none fit.";
        var user = $"Company: {companyName}\nSector: {sector}\n" +
            $"Source industry label: {sourceLabel}\nAllowed sub-industries: {allowed}";

        // The user's chosen parsing model may be a reasoning model that spends tokens BEFORE the answer.
        // A tight budget gets cut off mid-reasoning -> empty/truncated content -> JSON parse fails -> null.
        // The flash tier reasons especially hard when there's no source label to anchor on, so give it
        // generous headroom (reasoning + the one-line JSON). fast: true -> the provider's cheap/quick tier
        // (this is a constrained one-of-N pick, not a job for the pro model). Industry is a best-effort
        // enrichment (callers leave it null on a miss), so treat a user without a key the same as the model
        // being unreachable — return null, don't block the create/link/backfill flow.
        string raw;
        try { raw = await _llm.CompleteAsync(system, user, maxTokens: 900, jsonObject: true, fast: true, ct: ct); }
        catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { return null; }

        // Parse + validate; on any failure, log WHY (truncation vs out-of-list vs wrong-sector vs the model
        // genuinely answering null) so a "no fit" can be diagnosed instead of guessed at.
        var (result, reason) = Parse(raw, sector);
        if (result is { } ok) return ok;

        _logger.LogInformation("Classify no-fit: {Company} [{Sector}] label='{Label}' — {Reason}. raw='{Raw}'",
            companyName, sector, sourceLabel ?? "(none)", reason, Trim(raw, 200));
        return null;
    }

    // Returns the validated sub-industry, or null with a human-readable reason for the miss.
    private static (GicsSubIndustry? Result, string Reason) Parse(string raw, Sector sector)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, "empty reply (likely truncated — token budget hit before the JSON)");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("subIndustry", out var el))
                return (null, "no 'subIndustry' field in reply");
            if (el.ValueKind == JsonValueKind.Null)
                return (null, "model answered null (none of the sector's sub-industries fit)");
            if (el.ValueKind != JsonValueKind.String)
                return (null, $"'subIndustry' not a string ({el.ValueKind})");

            var value = el.GetString();
            if (!Enum.TryParse<GicsSubIndustry>(value, out var parsed))
                return (null, $"out-of-list value '{value}'");
            if (parsed.GetSector() != sector)
                return (null, $"wrong-sector pick '{value}' (rolls up to {parsed.GetSector()}, not {sector})");
            return (parsed, "ok");
        }
        catch (JsonException) { return (null, "malformed JSON"); }
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
