using System.Text.Json;
using Microsoft.Extensions.Logging;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Classification;

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

    public async Task<GicsSubIndustry?> ResolveSubIndustryAsync(Sector? sector, string? fmpLabel, string? companyName,
        string? description = null, bool bypassCache = false, CancellationToken ct = default)
    {
        // A vendor label maps to the same sub-industry for every company, so look it up in the learned
        // cache first — that's what keeps each distinct label to a single model call, ever. The on-demand
        // "Resolve with AI" path sets bypassCache so it always RE-REASONS (the whole point of the button is
        // to override a cached value), and below it neither reads nor re-writes the cache.
        if (!bypassCache && !string.IsNullOrWhiteSpace(fmpLabel) && _mappings.Get(fmpLabel) is { } cached)
            return cached;

        var (sub, cacheable) = await ClassifyAsync(sector, companyName, fmpLabel, description, ct);

        // Cache the learned label -> sub ONLY for a clean constrained match. A stage-2 fallback result is
        // driven by the company's name/description because the label was ambiguous or misleading for THIS
        // company (e.g. FMP labels Public Storage "REIT - Industrial"); caching it by label would mis-map
        // the next company that shares the label. So those stay uncached and are re-reasoned each time.
        // A bypassCache (manual) re-resolve also doesn't write — it's a per-company judgement, not a label rule.
        if (sub is { } resolved && cacheable && !bypassCache && !string.IsNullOrWhiteSpace(fmpLabel))
            _mappings.Set(fmpLabel, resolved);

        return sub;
    }

    // Returns the resolved sub-industry and whether it's safe to cache by label (true only for a clean
    // constrained stage-1 hit; a name-driven fallback is not label-stable).
    private async Task<(GicsSubIndustry? Sub, bool Cacheable)> ClassifyAsync(Sector? sector, string? companyName,
        string? sourceLabel, string? description, CancellationToken ct)
    {
        // Stage 1 — when we have a (trusted) source sector, constrain the model to that sector's
        // sub-industries: a cheap guardrail so a stray pick can't land in the wrong sector.
        if (sector is { } sec && await PickAsync(sec, companyName, sourceLabel, description, ct) is { } within)
            return (within, true);

        // Stage 2 — UNCONSTRAINED fallback across every sub-industry. Reached when there was no source
        // sector at all, OR stage 1 found no fit. The latter usually means the source sector itself is
        // wrong: vendor taxonomies disagree with GICS (FMP files First Solar under "Energy/Solar", but
        // GICS has no solar sub-industry in Energy — it rolls up under IT/Utilities). Reasoning from the
        // company's identity (name + description + label) lets the model pick the right home; its sector
        // and industry then roll up deterministically, self-healing the bad source sector at the caller.
        return (await PickAsync(restrictTo: null, companyName, sourceLabel, description, ct), false);
    }

    // One model call. `restrictTo` non-null = constrained to that sector's sub-industries (with a
    // wrong-sector re-check); null = unconstrained over all sub-industries (the chosen sub defines its
    // own sector). Returns the validated sub-industry, or null (logged with the reason).
    private async Task<GicsSubIndustry?> PickAsync(Sector? restrictTo, string? companyName, string? sourceLabel,
        string? description, CancellationToken ct)
    {
        var candidates = restrictTo is { } sec
            ? Enum.GetValues<GicsSubIndustry>().Where(i => i.GetSector() == sec).ToList()
            : Enum.GetValues<GicsSubIndustry>().ToList();
        if (candidates.Count == 0) return null;

        var allowed = string.Join(", ", candidates.Select(c => c.ToString()));
        var system = "You classify a company into exactly one GICS sub-industry. Reply ONLY with JSON " +
            "{\"subIndustry\":\"ENUM_NAME\"} where ENUM_NAME is exactly one of the allowed values, " +
            "or {\"subIndustry\":null} if none fit.";
        var user = $"Company: {companyName}\n" +
            (restrictTo is { } s ? $"Sector: {s}\n" : "") +
            $"Source industry label: {sourceLabel}\n" +
            (string.IsNullOrWhiteSpace(description) ? "" : $"Description: {Trim(description, 800)}\n") +
            $"Allowed sub-industries: {allowed}";

        // BOTH stages use the pro model (fast: false). The flash tier looked tempting for the constrained
        // stage 1 (a one-of-N pick within a known sector), but it made silent *sibling* errors — picking a
        // plausible neighbour in the right sector (Self-Storage labelled Industrial REIT; a brokerage
        // labelled Diversified Capital Markets). Those land as a confident "Resolved" and never surface,
        // and the label->sub mapping is CACHED, so a flash mistake is permanent. The cost argues for pro
        // anyway: each distinct label is resolved exactly once ever (the cache), so it's a bounded one-time
        // spend, not per-company. maxTokens headroom matters because a reasoning model spends tokens BEFORE
        // the JSON; a tight budget truncates mid-reasoning -> empty content -> parse fails -> null. Industry
        // is best-effort, so a missing key / unreachable model just returns null rather than blocking the flow.
        string raw;
        try { raw = await _llm.CompleteAsync(system, user, maxTokens: 900, jsonObject: true, fast: false, ct: ct); }
        catch (Exception ex) when (ex is HttpRequestException or MissingApiKeyException) { return null; }

        // Parse + validate; on any failure, log WHY (truncation vs out-of-list vs wrong-sector vs the model
        // genuinely answering null) so a "no fit" can be diagnosed instead of guessed at.
        var (result, reason) = Parse(raw, restrictTo);
        if (result is { } ok) return ok;

        _logger.LogInformation("Classify no-fit: {Company} [{Sector}] label='{Label}' — {Reason}. raw='{Raw}'",
            companyName, restrictTo?.ToString() ?? "(unconstrained)", sourceLabel ?? "(none)", reason, Trim(raw, 200));
        return null;
    }

    // Returns the validated sub-industry, or null with a human-readable reason for the miss. The
    // wrong-sector re-check only applies to a constrained pick (restrictTo non-null); the unconstrained
    // fallback deliberately accepts a sub from any sector.
    private static (GicsSubIndustry? Result, string Reason) Parse(string raw, Sector? restrictTo)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return (null, "empty reply (likely truncated — token budget hit before the JSON)");

        try
        {
            using var doc = JsonDocument.Parse(raw);
            if (!doc.RootElement.TryGetProperty("subIndustry", out var el))
                return (null, "no 'subIndustry' field in reply");
            if (el.ValueKind == JsonValueKind.Null)
                return (null, "model answered null (no fitting sub-industry)");
            if (el.ValueKind != JsonValueKind.String)
                return (null, $"'subIndustry' not a string ({el.ValueKind})");

            var value = el.GetString();
            if (!Enum.TryParse<GicsSubIndustry>(value, out var parsed))
                return (null, $"out-of-list value '{value}'");
            if (restrictTo is { } sector && parsed.GetSector() != sector)
                return (null, $"wrong-sector pick '{value}' (rolls up to {parsed.GetSector()}, not {sector})");
            return (parsed, "ok");
        }
        catch (JsonException) { return (null, "malformed JSON"); }
    }

    private static string Trim(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
