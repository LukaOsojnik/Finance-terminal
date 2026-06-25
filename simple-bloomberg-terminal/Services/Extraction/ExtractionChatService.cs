using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

public class ExtractionChatService : IExtractionChatService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly ISec2MdClient _sec2md;
    private readonly IFilingExtractionService _scan;
    private readonly IXbrlInstanceReader _instance;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    public ExtractionChatService(
        ICompanyRepository companies, IStockApiClient client, ISec2MdClient sec2md,
        IFilingExtractionService scan, IXbrlInstanceReader instance, IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _sec2md = sec2md;
        _scan = scan;
        _instance = instance;
        _llm = llm;
        _cache = cache;
    }


    private const int ContextBudgetChars = 60_000;   // ~15k tokens of filing text per turn (cached)

    // Appended to every node's system prompt: how to route information that belongs to ANOTHER segment.
    // The source agent stays pure — it never learns the target's save schema, only this tiny block.
    // The front-end parses ```handoff``` like it parses ```save``` and spawns the target segment's
    // agent, seeded with `seed`. See docs/extraction/cross-extraction.md.
    private const string HandoffSuffix =
        "\n\nIf the user surfaces information that belongs to a DIFFERENT segment (e.g. a supplier or " +
        "cost detail while you are the REVENUE or RISK analyst; a customer or revenue detail while you " +
        "are COST or RISK; a disclosed risk while you are COST or REVENUE), do NOT try to save it " +
        "yourself — you don't own that segment's schema. Instead emit a fenced block exactly like:\n" +
        "```handoff\n{\"node\":\"COST\",\"seed\":\"\"}\n```\n" +
        "node is the target segment, exactly one of COST, REVENUE, RISK. seed is a self-contained " +
        "instruction for that segment's analyst: what the user wants recorded AND the verbatim source " +
        "passage backing it (quote it from the findings above), since that analyst cannot see this " +
        "segment's text. Emit one handoff block per cross-segment item, alongside your normal reply.";

    // Appended INSTEAD of HandoffSuffix when THIS turn IS a hand-off you are receiving. The routing
    // decision is already made — your job is to record, not to re-route or second-guess the segment.
    private const string HandoffReceiverSuffix =
        "\n\nIMPORTANT — this is a CROSS-SEGMENT HAND-OFF you are RECEIVING. Another segment's analyst " +
        "has already determined this item belongs to YOUR segment and routed it to you with the " +
        "verbatim source text. Do NOT hand it off again, do NOT route it elsewhere, and do NOT " +
        "question the routing — your only job is to RECORD it here. Emit a ```save``` block now using " +
        "the schema above, putting the supplied passage in `reference` and the relevant `proof` fields. " +
        "The item may be a qualitative relationship (a supplier/customer/counterparty dependency) with " +
        "NO dollar figure — that is expected and valid: set `value` and `percentage` to null, name the " +
        "counterparty in `related_company`, and still emit the save block. Then confirm in one sentence " +
        "what you saved.";

    // The lead-analyst system prompt, tailored to the node being built. The save-block schema must
    // match what the page's normalizeSave() reads (revenue/cost: money fields; risk: scope + note).
    // handoff=true swaps the "emit a handoff" suffix for the "you are receiving one — record it" suffix.
    private static string SystemFor(ExtractionNode node, bool handoff) =>
        BaseSystemFor(node) + (handoff ? HandoffReceiverSuffix : HandoffSuffix);

    private static string BaseSystemFor(ExtractionNode node) => node switch
    {
        ExtractionNode.COST =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the COST candidates below, each with the VERBATIM proof text they " +
            "found. You are ALSO given the authoritative tagged XBRL figures for this filing's period. " +
            "Ground every claim in those findings (or the raw excerpts, if findings are absent); if " +
            "something isn't there, say so rather than guessing. The tagged XBRL figures are the " +
            "audited numbers — PREFER them for `value`; use the workers' prose for the name, segment " +
            "and supplier. When a prose figure disagrees with the tagged figure for the same line, " +
            "flag it rather than silently choosing. Help the user review and decide which cost sources " +
            "(cost lines, segments, key suppliers) and counterparty relationships to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific cost, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"COGS\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"related_company_ticker\":null,\"reference\":null," +
            "\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}\n```\n" +
            "classification is exactly one of COGS, OPEX, TOTAL_COSTS. value is absolute US dollars " +
            "(prefer the tagged XBRL figure; scale any 'in thousands/millions'); percentage is 0-100; " +
            "use null when not stated. related_company is a named supplier/counterparty (else null); " +
            "when it's a publicly traded company you can identify, also set related_company_ticker to " +
            "its stock ticker (else null) so it can be enriched. reference is the verbatim passage " +
            "(name the SEC Item or note, then the source text) this whole cost record was drawn from. " +
            "Each proof field is the VERBATIM excerpt substring backing that one field (null for " +
            "fields you left null). Emit one save block per cost the user confirms, alongside your " +
            "normal reply.",

        ExtractionNode.RISK =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the RISK candidates below, each with the VERBATIM proof text they " +
            "found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. Help the user review and " +
            "decide which disclosed risks to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific risk, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"BUSINESS\",\"note\":null,\"reference\":null," +
            "\"proof\":{\"name\":\"\",\"classification\":null,\"note\":null}}\n```\n" +
            "classification is the risk scope, exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, " +
            "LEGAL_REGULATORY, FINANCIAL, GENERAL. note is one or two sentences summarising the risk; " +
            "use null when not stated. reference is the verbatim passage (name the SEC Item — 1A risk " +
            "factors / 7A market risk — then the source text) this whole risk record was drawn from. " +
            "Each proof field is the VERBATIM excerpt substring backing it " +
            "(null for fields you left null). Emit one save block per risk the user confirms, " +
            "alongside your normal reply.",

        _ =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the revenue candidates below, each with the VERBATIM proof text " +
            "they found. You are ALSO given the authoritative tagged XBRL figures for this filing's " +
            "period. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. The tagged XBRL figures " +
            "are the audited numbers — PREFER them for `value`; use the workers' prose for the name, " +
            "segment and customer. When a prose figure disagrees with the tagged figure for the same " +
            "line, flag it rather than silently choosing. Help the user review and decide which revenue " +
            "sources (segments, products, regions, major customers) and counterparty relationships to " +
            "keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific source, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"PRODUCT\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"related_company_ticker\":null,\"reference\":null," +
            "\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}\n```\n" +
            "classification is exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT. value is absolute US " +
            "dollars (prefer the tagged XBRL figure; scale any 'in thousands/millions'); percentage is " +
            "0-100; use null when not stated. " +
            "related_company is a named customer/counterparty (else null); when it's a publicly traded " +
            "company you can identify, also set related_company_ticker to its stock ticker (else null) " +
            "so it can be enriched. reference is the verbatim passage (name the SEC Item or note, then " +
            "the source text) this whole revenue record was drawn from. " +
            "Each proof field is the VERBATIM excerpt substring backing it (null for fields you left " +
            "null). Emit one save block per source the user confirms, alongside your normal reply.",
    };

    public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history, string? filingType = null, bool handoff = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // The parallel worker scan runs (once per filing) before the main agent can answer — tell the
        // user so the first turn isn't a silent wait while 36 chunks fan out. A hand-off never scans
        // (the source agent already found the fact), so it never shows this status.
        var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
        if (hasFiling && !handoff && !_cache.TryGetValue(FilingExtractionService.FindingsKey(accession, doc, node), out _))
            yield return new ChatDelta("status", "Scanning the filing with parallel worker agents…");

        var grounding = await GroundingAsync(companyId, accession, doc, node, filingType, scanIfMissing: !handoff, ct);

        var messages = new List<DeepSeekMessage> { new("system", SystemFor(node, handoff) + grounding) };
        foreach (var m in history)
            messages.Add(new DeepSeekMessage(m.Role == "assistant" ? "assistant" : "user", m.Content));

        // No maxTokens → the lead-analyst reply runs to the model's own ceiling instead of being cut
        // off mid-answer at a fixed cap.
        await foreach (var delta in _llm.StreamAsync(messages, ct: ct))
            yield return delta;
    }

    // The main agent's grounding: the workers' findings digest (auto-scan, or a curated heading scan
    // the user kicked off), falling back to raw section excerpts only if the scan returned nothing —
    // plus, for the numeric nodes (COST, REVENUE), the authoritative tagged XBRL figures for the
    // filing's period (the "calculator" the agent should prefer over any number it read in the prose).
    private async Task<string> GroundingAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType,
        bool scanIfMissing, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc)) return "";

        // scanIfMissing=false (hand-off): read whatever the workers already cached for this segment, but
        // never fan out a fresh scan — the source agent already found the fact we're recording.
        var digest = scanIfMissing
            ? await _scan.GetOrScanDigestAsync(companyId, accession, doc, node, filingType, ct)
            : _cache.TryGetValue(FilingExtractionService.FindingsKey(accession, doc, node), out string? cached) ? (cached ?? "") : "";
        var prose = !string.IsNullOrEmpty(digest)
            ? digest
            : await RawFallbackAsync(companyId, accession, doc, node, filingType, ct);

        // Numeric nodes get the audited tagged figures as a third feed (the "calculator"); RISK is
        // pure prose, so no XBRL block. The structured view is the single source — formatted to text
        // here for the agent, projected to a table for the UI. See docs/extraction-pipeline-v2.md §3-§5.
        var view = await GetXbrlViewAsync(companyId, accession, node, ct);
        var xbrl = view is null ? "" : FormatXbrlText(view);

        var parts = new[] { prose, xbrl }.Where(p => !string.IsNullOrEmpty(p)).ToList();
        return parts.Count == 0 ? "" : "\n\n" + string.Join("\n\n", parts);
    }

    // The structured audited XBRL facts for one filing+node (COST/REVENUE only) — the single source the
    // grounding text AND the UI both render from. Cached per filing (one SEC round-trip, not per chat
    // turn / poll); a null view (RISK, fetch failure, or nothing tagged) is cached too so we don't retry.
    public async Task<XbrlView?> GetXbrlViewAsync(
        long companyId, string accession, ExtractionNode node, CancellationToken ct = default)
    {
        if (node is not (ExtractionNode.COST or ExtractionNode.REVENUE)) return null;
        var key = $"xbrl-view:{node}:{accession}";
        if (_cache.TryGetValue(key, out XbrlView? cached)) return cached;

        var view = node == ExtractionNode.COST
            ? await BuildCostXbrlViewAsync(companyId, accession, ct)
            : await BuildRevenueXbrlViewAsync(companyId, accession, ct);
        _cache.Set(key, view, TimeSpan.FromMinutes(30));
        return view;
    }

    // Company facts + the filing's report-date period for the company under extraction. Null when the
    // company has no CIK or companyfacts is unreachable (XBRL is an enrichment — never fatal). The
    // period end ties the figures to the year THIS filing reported, not the newest on file.
    private async Task<(EdgarFacts Facts, string Cik, string? End)?> LoadFilingFactsAsync(
        long companyId, string accession, CancellationToken ct)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return null;
        var cik10 = Cik.Pad(Cik.Normalize(company.Cik) ?? company.Cik);

        EdgarCompanyFacts? facts;
        try
        {
            facts = await _client.GetCompanyFacts(cik10);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
        if (facts?.Facts is null) return null;

        var end = await PeriodEndAsync(cik10, accession, ct);
        return (facts.Facts, company.Cik!, end);
    }

    // COST: company-total cost concepts (companyfacts) + per-segment implied cost (instance doc), plus
    // the Σ-segment-revenue-vs-total tie check. Each segment carries the revenue−OI detail + reconcile flag.
    private async Task<XbrlView?> BuildCostXbrlViewAsync(long companyId, string accession, CancellationToken ct)
    {
        var loaded = await LoadFilingFactsAsync(companyId, accession, ct);
        if (loaded is null) return null;
        var (facts, cik, end) = loaded.Value;

        var cogs = XbrlFacts.AnnualForEnd(facts, end, XbrlFacts.Cogs);
        var opex = XbrlFacts.AnnualForEnd(facts, end, XbrlFacts.Opex);
        var segments = await SegmentCostsAsync(cik, accession, end, ct);

        if (cogs is null && opex is null && segments.Count == 0) return null;

        var totals = new List<XbrlFactLine>();
        if (cogs is not null) totals.Add(new XbrlFactLine("Cost of revenue", cogs.Val));
        if (opex is not null) totals.Add(new XbrlFactLine("Operating expenses", opex.Val));

        var segLines = segments
            .Select(s => new XbrlSegmentLine(s.Segment, s.Cost,
                $"revenue {s.Revenue:N0} − operating income {s.OperatingIncome:N0}", s.Reconciles))
            .ToList();

        var totalRev = XbrlFacts.AnnualForEnd(facts, end, XbrlFacts.Revenue);
        var sumCheck = SumCheck(segments.Sum(s => s.Revenue), totalRev, segments.Count);
        var period = cogs?.End ?? opex?.End ?? end;
        return new XbrlView("COST", period, totals, segLines, sumCheck);
    }

    // Per-segment cost from the filing's instance document, guarded so an instance fetch/parse failure
    // (or a filing with no segment tagging) simply yields no segment lines.
    private async Task<IReadOnlyList<SegmentCost>> SegmentCostsAsync(
        string cik, string accession, string? end, CancellationToken ct)
    {
        try
        {
            return await _instance.SegmentCostsAsync(Cik.Trim(cik), accession.Replace("-", ""), end, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    // REVENUE: company-total revenue (companyfacts) + per-segment revenue (instance doc, the same figure
    // SegmentCostsAsync uses as its minuend — no subtraction), plus the Σ tie check.
    private async Task<XbrlView?> BuildRevenueXbrlViewAsync(long companyId, string accession, CancellationToken ct)
    {
        var loaded = await LoadFilingFactsAsync(companyId, accession, ct);
        if (loaded is null) return null;
        var (facts, cik, end) = loaded.Value;

        var total = XbrlFacts.AnnualForEnd(facts, end, XbrlFacts.Revenue);
        var segments = await SegmentRevenuesAsync(cik, accession, end, ct);

        if (total is null && segments.Count == 0) return null;

        var totals = total is null ? new List<XbrlFactLine>() : [new XbrlFactLine("Total revenue", total.Val)];
        // Revenue is the tagged figure itself — no subtraction, so every segment trivially reconciles.
        var segLines = segments.Select(s => new XbrlSegmentLine(s.Segment, s.Revenue, null, true)).ToList();
        var sumCheck = SumCheck(segments.Sum(s => s.Revenue), total, segments.Count);
        var period = total?.End ?? end;
        return new XbrlView("REVENUE", period, totals, segLines, sumCheck);
    }

    // The ÎŁ-segment-revenue vs company-total-revenue tie test (within 1%), shared by both nodes. Null
    // when there are no segments or no total to compare against.
    private static XbrlSumCheck? SumCheck(double segmentRevenueSum, EdgarFact? totalRevenue, int segmentCount) =>
        segmentCount > 0 && totalRevenue?.Val is { } tr && tr > 0
            ? new XbrlSumCheck(segmentRevenueSum, tr, Math.Abs(segmentRevenueSum - tr) <= tr * 0.01)
            : null;

    // Render the structured view back into the grounding prose the lead-analyst agent reads. Node-aware:
    // COST shows the implied-cost detail + reconcile warnings; REVENUE shows the tagged figure directly.
    private static string FormatXbrlText(XbrlView v)
    {
        var noun = v.Node == "COST" ? "cost" : "revenue";
        var sb = new StringBuilder(
            $"XBRL TAGGED FACTS (us-gaap, audited — the authoritative {noun} figures for this filing" +
            (v.PeriodEnd is null ? "" : $", period ending {v.PeriodEnd}") + "):\n");
        foreach (var t in v.Totals)
            sb.Append("- ").Append(t.Label).Append(": ").Append(t.Value?.ToString("N0") ?? "n/a").Append(" USD\n");

        if (v.Segments.Count > 0)
        {
            sb.Append(v.Node == "COST"
                ? "\nPER-SEGMENT COST (from the XBRL instance; segment revenue − segment operating income, " +
                  "the implied cost to run each reported business segment):\n"
                : "\nPER-SEGMENT REVENUE (from the XBRL instance, the tagged revenue of each reported business segment):\n");
            foreach (var s in v.Segments)
            {
                if (v.Node == "COST")
                    sb.Append("- ").Append(s.Segment).Append(": cost ").Append(s.Value.ToString("N0"))
                      .Append(" USD (").Append(s.Detail).Append(')')
                      .Append(s.Reconciles ? "" : "  ⚠ DOESN'T RECONCILE (implied cost outside [0, revenue]) — treat as unreliable");
                else
                    sb.Append("- ").Append(s.Segment).Append(": ").Append(s.Value.ToString("N0")).Append(" USD");
                sb.Append('\n');
            }

            if (v.SumCheck is { } c)
                sb.Append("ÎŁ segment revenue ").Append(c.SegmentSum.ToString("N0"))
                  .Append(" vs company-total revenue ").Append(c.Total.ToString("N0")).Append(" — ")
                  .Append(c.Ties ? "ties." : "differs (segments may be incomplete).")
                  .Append('\n');
        }

        sb.Append(
            $"Prefer these tagged numbers for `value`. If a {noun} figure you read in the prose disagrees " +
            "with the tagged figure for the same line, say so rather than silently picking one.");
        return sb.ToString();
    }

    // Per-segment revenue from the filing's instance document, guarded like SegmentCostsAsync.
    private async Task<IReadOnlyList<SegmentRevenue>> SegmentRevenuesAsync(
        string cik, string accession, string? end, CancellationToken ct)
    {
        try
        {
            return await _instance.SegmentRevenuesAsync(Cik.Trim(cik), accession.Replace("-", ""), end, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    // The report date (period end) for one accession, from the company's submissions index. Null when
    // submissions don't resolve or the accession isn't in the recent set — the caller then defaults to
    // the latest annual fact.
    private async Task<string?> PeriodEndAsync(string cik10, string accession, CancellationToken ct)
    {
        EdgarSubmissions? subs;
        try
        {
            subs = await _client.GetSubmissions(cik10);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }

        var recent = subs?.Filings?.Recent;
        var accns = recent?.AccessionNumber;
        var ends = recent?.ReportDate;
        if (accns is null || ends is null) return null;

        var i = accns.IndexOf(accession);
        return i >= 0 && i < ends.Count ? ends[i] : null;
    }

    // Used only when the scan yields nothing: the cleaned node-target Item excerpts, budgeted per
    // section (Items 7/8 for revenue & cost, Items 1A/7A for risk). Prefers sec2md markdown, falling
    // back to raw SEC HTML when the sidecar is down (FilingSections reads both).
    private async Task<string> RawFallbackAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType, CancellationToken ct)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return "";
        var cik = Cik.Trim(company.Cik);
        var acc = accession.Replace("-", "");
        var raw = await _sec2md.ToMarkdownAsync(cik, acc, doc, filingType, ct)
                  ?? await _client.GetFilingDocument(cik, acc, doc);
        if (raw is null) return "";
        var items = FilingSections.ItemsFor(node);
        var context = BuildContext(raw, items);
        return string.IsNullOrEmpty(context)
            ? ""
            : $"FILING EXCERPTS (Items {string.Join(", ", items)}):\n{context}";
    }

    private static string BuildContext(string raw, string[] items)
    {
        var chunks = FilingSections.Build(raw, items);
        if (chunks.Count == 0) return "";

        // Split the budget evenly across the sections present, so one long section can't crowd out
        // the others. Chunks already arrive in the node's item-priority order.
        var sections = chunks.Select(c => c.Section).Distinct().ToList();
        int perSection = ContextBudgetChars / sections.Count;
        var used = sections.ToDictionary(s => s, _ => 0);

        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (used[chunk.Section] + chunk.Text.Length > perSection) continue;   // this section is full
            used[chunk.Section] += chunk.Text.Length;
            sb.Append('[').Append(chunk.Section).Append("]\n").Append(chunk.Text).Append("\n\n");
        }
        return sb.ToString();
    }
}
