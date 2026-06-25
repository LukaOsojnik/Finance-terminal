using System.Text.Json;
using System.Text.RegularExpressions;

namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>
/// One business segment's implied cost for a filing period: <c>Cost = Revenue − OperatingIncome</c>.
/// <see cref="Reconciles"/> is the subtraction-sanity check (0 ≤ Cost ≤ Revenue) — false means the
/// matched profit measure is wrong (e.g. a non-GAAP segment figure), so the figure is unreliable.
/// </summary>
public record SegmentCost(string Segment, double Revenue, double OperatingIncome, double Cost, bool Reconciles);

/// <summary>One business segment's tagged revenue for a filing period — the per-segment figure the
/// dimensionless <c>companyfacts</c> endpoint can't carry, read straight from the instance document
/// (no subtraction, unlike <see cref="SegmentCost"/>).</summary>
public record SegmentRevenue(string Segment, double Revenue);

/// <summary>
/// Reads PER-SEGMENT cost out of a filing's XBRL <em>instance document</em> — the dimensional data the
/// dimensionless <c>companyfacts</c> endpoint (see <see cref="XbrlFacts"/>) can't carry. Discovers the
/// instance file in the filing folder, then pairs each business-segment's tagged revenue and operating
/// income (joined by XBRL <c>contextRef</c>) to imply the segment's cost. Phase 2 of docs/cost-extraction.md.
/// </summary>
public interface IXbrlInstanceReader
{
    // Per-segment cost for the filing's reporting period (<paramref name="periodEnd"/>, the report
    // date; null => the latest segment period present in the instance). Empty when the filing tags no
    // business segments, or the instance can't be fetched/parsed (an enrichment, never fatal).
    Task<IReadOnlyList<SegmentCost>> SegmentCostsAsync(
        string cikTrimmed, string accessionNoDashes, string? periodEnd, CancellationToken ct = default);

    // Per-segment revenue for the filing's reporting period — the same dimensional figure used as the
    // minuend in <see cref="SegmentCostsAsync"/>, surfaced directly (no operating-income subtraction)
    // for the REVENUE node's XBRL grounding. Same best-effort contract as above.
    Task<IReadOnlyList<SegmentRevenue>> SegmentRevenuesAsync(
        string cikTrimmed, string accessionNoDashes, string? periodEnd, CancellationToken ct = default);
}

public class XbrlInstanceReader(IStockApiClient client) : IXbrlInstanceReader
{
    // us-gaap concepts a filer tags segment revenue under, in preference order (first present wins).
    private static readonly string[] RevenueConcepts =
        ["RevenueFromContractWithCustomerExcludingAssessedTax", "Revenues", "RevenueFromContractWithCustomerIncludingAssessedTax"];
    // The clean GAAP segment profit measure; Cost = Revenue − this.
    private const string ProfitConcept = "OperatingIncomeLoss";

    public async Task<IReadOnlyList<SegmentCost>> SegmentCostsAsync(
        string cikTrimmed, string accessionNoDashes, string? periodEnd, CancellationToken ct = default)
    {
        var xml = await FetchInstanceXmlAsync(cikTrimmed, accessionNoDashes, ct);
        return string.IsNullOrEmpty(xml) ? [] : ParseSegmentCosts(xml, periodEnd);
    }

    public async Task<IReadOnlyList<SegmentRevenue>> SegmentRevenuesAsync(
        string cikTrimmed, string accessionNoDashes, string? periodEnd, CancellationToken ct = default)
    {
        var xml = await FetchInstanceXmlAsync(cikTrimmed, accessionNoDashes, ct);
        return string.IsNullOrEmpty(xml) ? [] : ParseSegmentRevenues(xml, periodEnd);
    }

    // Discover + fetch the filing's XBRL instance document. Best-effort: a discovery/fetch failure
    // yields null so the segment readers simply return no rows (an enrichment, never fatal).
    private async Task<string?> FetchInstanceXmlAsync(string cikTrimmed, string accessionNoDashes, CancellationToken ct)
    {
        var instanceName = await FindInstanceNameAsync(cikTrimmed, accessionNoDashes, ct);
        if (instanceName is null) return null;
        try
        {
            return await client.GetFilingDocument(cikTrimmed, accessionNoDashes, instanceName);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    // The instance file in the filing folder: the "*_htm.xml" XBRL document (NOT the _cal/_def/_lab/_pre
    // linkbases). Read from the folder's index.json so we don't guess the filename.
    private async Task<string?> FindInstanceNameAsync(string cikTrimmed, string accessionNoDashes, CancellationToken ct)
    {
        string? indexJson;
        try
        {
            indexJson = await client.GetFilingDocument(cikTrimmed, accessionNoDashes, "index.json");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        if (string.IsNullOrWhiteSpace(indexJson)) return null;

        try
        {
            using var doc = JsonDocument.Parse(indexJson);
            if (!doc.RootElement.TryGetProperty("directory", out var dir) ||
                !dir.TryGetProperty("item", out var items) || items.ValueKind != JsonValueKind.Array)
                return null;

            string? fallback = null;
            foreach (var it in items.EnumerateArray())
            {
                var name = it.TryGetProperty("name", out var n) ? n.GetString() : null;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (name.EndsWith("_htm.xml", StringComparison.OrdinalIgnoreCase)) return name;   // the instance
                // Fallback: a bare .xml that isn't a linkbase, in case the suffix convention differs.
                if (name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) &&
                    !Regex.IsMatch(name, @"_(cal|def|lab|pre)\.xml$", RegexOptions.IgnoreCase) &&
                    !name.Equals("FilingSummary.xml", StringComparison.OrdinalIgnoreCase))
                    fallback ??= name;
            }
            return fallback;
        }
        catch (JsonException) { return null; }
    }

    // Pure parse (no I/O), so it's unit-testable against a fixture. Pairs segment-level revenue +
    // operating income for the target period and derives the implied cost per segment.
    public static IReadOnlyList<SegmentCost> ParseSegmentCosts(string xml, string? periodEnd)
    {
        var contexts = SegmentContexts(xml);
        if (contexts.Count == 0) return [];
        var target = TargetPeriod(contexts, periodEnd);

        var revenue = FactsByContext(xml, RevenueConcepts);
        var profit = FactsByContext(xml, ProfitConcept);

        var result = new List<SegmentCost>();
        foreach (var (ctxId, ctx) in contexts)
        {
            if (ctx.End != target) continue;
            if (!revenue.TryGetValue(ctxId, out var rev) || !profit.TryGetValue(ctxId, out var oi)) continue;
            var cost = rev - oi;
            result.Add(new SegmentCost(PrettySegment(ctx.Member), rev, oi, cost, cost >= 0 && cost <= rev));
        }
        return result
            .GroupBy(s => s.Segment).Select(g => g.First())   // one row per segment at the target period
            .OrderByDescending(s => s.Revenue)
            .ToList();
    }

    // Pure parse: the tagged revenue for each business segment at the target period — the same facts
    // ParseSegmentCosts reads, without the operating-income subtraction.
    public static IReadOnlyList<SegmentRevenue> ParseSegmentRevenues(string xml, string? periodEnd)
    {
        var contexts = SegmentContexts(xml);
        if (contexts.Count == 0) return [];
        var target = TargetPeriod(contexts, periodEnd);

        var revenue = FactsByContext(xml, RevenueConcepts);

        var result = new List<SegmentRevenue>();
        foreach (var (ctxId, ctx) in contexts)
        {
            if (ctx.End != target) continue;
            if (!revenue.TryGetValue(ctxId, out var rev)) continue;
            result.Add(new SegmentRevenue(PrettySegment(ctx.Member), rev));
        }
        return result
            .GroupBy(s => s.Segment).Select(g => g.First())   // one row per segment at the target period
            .OrderByDescending(s => s.Revenue)
            .ToList();
    }

    // contextId -> (segment member, period end), for contexts carrying EXACTLY the segments axis
    // (a context that also has a product axis is a sub-breakdown, not the segment total — skip it).
    private static Dictionary<string, (string Member, string End)> SegmentContexts(string xml)
    {
        var contexts = new Dictionary<string, (string Member, string End)>();
        foreach (Match m in Regex.Matches(xml, "(?s)<(?:[\\w-]+:)?context[^>]*id=\"([^\"]+)\"[^>]*>(.*?)</(?:[\\w-]+:)?context>"))
        {
            var body = m.Groups[2].Value;
            if (!body.Contains("BusinessSegmentsAxis") || body.Contains("ProductOrServiceAxis")) continue;
            var member = Regex.Match(body, "BusinessSegmentsAxis\">[^:]*:([^<]+?)Member<").Groups[1].Value;
            var end = Regex.Match(body, "<(?:\\w+:)?endDate>([^<]+)</(?:\\w+:)?endDate>").Groups[1].Value;
            if (member.Length > 0 && end.Length > 0) contexts[m.Groups[1].Value] = (member, end);
        }
        return contexts;
    }

    // Target the filing's period; default to the latest segment period the instance carries.
    private static string TargetPeriod(Dictionary<string, (string Member, string End)> contexts, string? periodEnd) =>
        !string.IsNullOrWhiteSpace(periodEnd) && contexts.Values.Any(c => c.End == periodEnd)
            ? periodEnd!
            : contexts.Values.Select(c => c.End).Max()!;

    // contextRef -> value for the first concept name present (us-gaap facts; values may be negative).
    private static Dictionary<string, double> FactsByContext(string xml, params string[] concepts)
    {
        foreach (var concept in concepts)
        {
            var map = new Dictionary<string, double>();
            // The namespace prefix carries a hyphen (us-gaap:), so allow '-' — \w alone would miss it.
            foreach (Match m in Regex.Matches(xml,
                $"<(?:[\\w-]+:)?{concept} [^>]*contextRef=\"([^\"]+)\"[^>]*>(-?[0-9]+(?:\\.[0-9]+)?)</(?:[\\w-]+:)?{concept}>"))
            {
                if (double.TryParse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture, out var v))
                    map[m.Groups[1].Value] = v;
            }
            if (map.Count > 0) return map;
        }
        return [];
    }

    // "AmericasSegment" -> "Americas"; "GreaterChinaSegment" -> "Greater China".
    private static string PrettySegment(string member)
    {
        var s = Regex.Replace(member, "Segment$", "");
        s = Regex.Replace(s, "(?<=[a-z])(?=[A-Z])", " ");
        return s.Length == 0 ? member : s;
    }
}
