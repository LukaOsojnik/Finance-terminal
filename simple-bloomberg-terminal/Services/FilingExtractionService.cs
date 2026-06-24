using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

public enum ScanChunkPhase { Planned, Running, Done, Error }

/// <summary>One sub-task in the parallel scan: its index in the flat chunk list, the Item it groups
/// under, and the heading titles the single worker call covers.</summary>
public record ScanChunkInfo(int Index, string Item, IReadOnlyList<string> Titles);

/// <summary>A live scan progress event. <see cref="ScanChunkPhase.Planned"/> carries the full
/// <paramref name="Plan"/> once (so the UI can lay out the section tree); the later phases carry just
/// the chunk <paramref name="Index"/> (+ <paramref name="Found"/> on Done) to flip one row's status.</summary>
public record ScanProgress(
    ScanChunkPhase Phase, int Index, int Found, IReadOnlyList<ScanChunkInfo>? Plan,
    // Set on Done/Error: the verbatim prompt the worker saw and its raw reply, so the widget can show
    // "under the hood" what one agent call received and answered.
    string? Prompt = null, string? Response = null);

public class FilingExtractionService : IFilingExtractionService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly ISec2MdClient _sec2md;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    private const int MaxParallel = 6;   // concurrent worker calls in the map phase

    public FilingExtractionService(
        ICompanyRepository companies, IStockApiClient client, ISec2MdClient sec2md,
        IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _sec2md = sec2md;
        _llm = llm;
        _cache = cache;
    }

    // The chat grounds on this key; both the auto-scan and a curated heading scan write it. Keyed by
    // node so a filing's revenue, cost and risk digests don't overwrite one another.
    public static string FindingsKey(string accession, string doc, ExtractionNode node) =>
        $"filing-findings:{node}:{accession}:{doc}";
    private static string HeadingsKey(string accession, string doc, ExtractionNode node) =>
        $"filing-headings:{node}:{accession}:{doc}";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    // The worker system prompt, tailored to the node being built. Revenue and cost share a shape
    // (name + classification + money + counterparty); risk swaps money/counterparty for a free-text
    // note and a scope bucket. All three return the same {"sources":[...]} envelope so Parse is shared.
    private static string SystemFor(ExtractionNode node) => node switch
    {
        ExtractionNode.COST =>
            "You extract COST sources for a single US public company from one excerpt of its SEC " +
            "filing. Return ONLY the costs clearly evidenced in THIS excerpt — do not guess or carry " +
            "over outside knowledge. Focus on the cost LABEL, its segment, and any NAMED SUPPLIER or " +
            "raw-material dependence; the exact company-total dollar figures are sourced separately " +
            "from tagged XBRL, so prioritise getting the name/segment/supplier and proof right over " +
            "transcribing big totals. For each cost provide: name (the cost line / segment / supplier " +
            "label), classification (exactly one of COGS, OPEX, TOTAL_COSTS), value (cost in absolute " +
            "US dollars — scale any 'in thousands/millions' to the full number; null if not stated), " +
            "percentage (share of total cost or revenue 0-100, null if not stated), related_company (a " +
            "named supplier/counterparty if the row is about one, else null). For every field you " +
            "fill, include in 'proof' the VERBATIM substring of this excerpt that backs it (null for " +
            "fields you left null). Reply with JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"name\":\"\",\"classification\":\"\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}]}. If the excerpt names no cost " +
            "source, reply {\"sources\":[]}.",

        ExtractionNode.RISK =>
            "You extract RISKS a single US public company discloses, from one excerpt of its SEC " +
            "filing (Item 1A risk factors / Item 7A market risk). Return ONLY risks clearly evidenced " +
            "in THIS excerpt — do not guess or carry over outside knowledge. For each risk provide: " +
            "name (a short label for the risk), classification (its scope, exactly one of " +
            "MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL), note (one or " +
            "two sentences summarising the risk in plain language). For every field you fill, include " +
            "in 'proof' the VERBATIM substring of this excerpt that backs it (null for fields you left " +
            "null). Reply with JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"name\":\"\",\"classification\":\"\",\"note\":null," +
            "\"proof\":{\"name\":\"\",\"classification\":null,\"note\":null}}]}. If the excerpt names " +
            "no risk, reply {\"sources\":[]}.",

        _ =>
            "You extract revenue sources for a single US public company from one excerpt of its SEC " +
            "filing. Return ONLY the sources clearly evidenced in THIS excerpt — do not guess or carry " +
            "over outside knowledge. Focus on the revenue LABEL and its breakdown — segment, product, " +
            "region or major customer; the exact company-total dollar figures are sourced separately " +
            "from tagged XBRL, so prioritise getting the name/segment/customer and proof right over " +
            "transcribing big totals. For each source provide: name (the segment / product / region / " +
            "major-customer label), classification (exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT), " +
            "value (revenue in absolute US dollars — scale any 'in thousands/millions' to the full " +
            "number; null if not stated), percentage (share of total revenue 0-100, null if not stated), " +
            "related_company (a named counterparty/customer if the row is about one, else null). For " +
            "every field you fill, include in 'proof' the VERBATIM substring of this excerpt that backs " +
            "it (null for fields you left null). Reply with JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"name\":\"\",\"classification\":\"\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}]}. If the excerpt names no revenue " +
            "source, reply {\"sources\":[]}.",
    };

    public async Task<IReadOnlyList<ExtractionSuggestion>> ExtractAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        CancellationToken ct = default)
    {
        var raw = await FetchRawAsync(companyId, accession, doc, filingType, ct);
        if (raw is null) return [];
        return await ScanChunksAsync(FilingSections.Build(raw, FilingSections.ItemsFor(node)), node, null, ct);
    }

    // The chat's grounding digest: cached per filing; built by the auto-scan on a miss (heading triage
    // + always-Item-8), so the chat sees the financial-statement figures whether or not the user
    // clicked auto-scan. ScanAutoAsync writes the FindingsKey itself; only when it surfaces nothing
    // (e.g. a plain-text filing with no detectable headings) do we fall back to the flat all-sections scan.
    public async Task<string> GetOrScanDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        CancellationToken ct = default)
    {
        if (_cache.TryGetValue(FindingsKey(accession, doc, node), out string? cached)) return cached ?? "";

        var auto = await ScanAutoAsync(companyId, accession, doc, node, filingType, ct: ct);
        if (auto.Found > 0 && _cache.TryGetValue(FindingsKey(accession, doc, node), out string? scanned))
            return scanned ?? "";

        var findings = await ExtractAsync(companyId, accession, doc, node, filingType, ct);
        var digest = findings.Count > 0 ? FormatDigest(findings, node) : "";
        _cache.Set(FindingsKey(accession, doc, node), digest, CacheFor);
        return digest;
    }

    // Mode B (auto) — the replacement for hand-picking sections: surface every bold heading, let a
    // cheap triage model read just the titles and choose the ones worth reading for this node, then
    // scan only those in parallel and stash the digest as the chat's grounding. No user picking.
    public async Task<AutoScanResult> ScanAutoAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        Action<ScanProgress>? onProgress = null, CancellationToken ct = default)
    {
        var headings = await GetOrParseHeadingsAsync(companyId, accession, doc, node, filingType, ct);
        var items = FilingSections.ItemsFor(node);

        // Heading triage drives the narrative items (e.g. Item 7 MD&A), where bold headings line up
        // with topic boundaries. Item 8 (financial statements) is handled separately, below.
        var picked = headings.Count > 0 ? await TriageHeadingsAsync(headings, node, ct) : [];
        var pickedSet = picked.ToHashSet();
        // Always scan all of Item 7 (MD&A): for revenue/cost we want its full segment narrative, which
        // triage can skip when judging by title. No-op for RISK (its sections are 1A/7A, not "Item 7").
        for (int i = 0; i < headings.Count; i++)
            if (headings[i].Section == "Item 7") pickedSet.Add(i);

        // Heading-based chunks for the picked headings — but NOT Item 8. In the financial statements the
        // tables are detached from their bold headings, so "nearest heading" mislabels them (a segment
        // revenue table lands under a tax note) and the per-heading cap truncates them.
        // Pack consecutive same-Item headings into one worker call up to the chunk budget: a tiny heading
        // body no longer wastes a whole LLM call — several small titles ride together, fewer calls.
        var pickedHeadings = pickedSet
            .OrderBy(i => i)
            .Where(i => headings[i].Section != "Item 8")
            .Select(i => headings[i])
            .ToList();
        var chunks = PackHeadings(pickedHeadings);

        // Item 8: sequential, document-order chunks of the whole section, so every table reaches a
        // worker intact and in place (no mis-attribution, no per-heading truncation). Markdown is cached
        // by FetchRawAsync, so this doesn't re-convert the filing.
        if (items.Contains("8") && await FetchRawAsync(companyId, accession, doc, filingType, ct) is { } raw)
            chunks.AddRange(FilingSections.BuildSection(raw, "8"));

        // The page's triage report: every heading offered + whether scanned. Item 8 is now read in full
        // sequentially, so its headings are all marked scanned.
        var report = headings
            .Select((h, i) => new ScannedHeading(h.Section, h.Title, h.Section == "Item 8" || pickedSet.Contains(i)))
            .ToList();

        // Announce the plan once (before any worker runs) so the widget can lay out the section tree;
        // the per-chunk Running/Done events below flip each row's status as the 6-wide pool drains.
        onProgress?.Invoke(new ScanProgress(ScanChunkPhase.Planned, -1, 0,
            chunks.Select((c, i) => new ScanChunkInfo(i, c.Item, c.Titles ?? [])).ToList()));

        var findings = chunks.Count > 0 ? await ScanChunksAsync(chunks, node, onProgress, ct) : [];
        var digest = findings.Count > 0 ? FormatDigest(findings, node) : "";
        _cache.Set(FindingsKey(accession, doc, node), digest, CacheFor);
        return new AutoScanResult(chunks.Count, findings.Count, report);
    }

    // Triage step: feed only the heading titles (cheap) to the scan model and let it pick the ids
    // worth reading in full. Falls back to every heading if triage returns nothing or is unreachable.
    private async Task<List<int>> TriageHeadingsAsync(
        IReadOnlyList<FilingHeading> headings, ExtractionNode node, CancellationToken ct)
    {
        var list = new StringBuilder();
        for (int i = 0; i < headings.Count; i++)
            list.Append(i).Append(": [").Append(headings[i].Section).Append("] ")
                .Append(headings[i].Title).Append('\n');

        try
        {
            var answer = await _llm.CompleteAsync(
                TriageSystemFor(node), $"Headings:\n{list}", maxTokens: 800, jsonObject: true, fast: true, ct: ct);
            var ids = ParseIds(answer, headings.Count);
            if (ids.Count > 0) return ids;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { /* fall through */ }

        return Enumerable.Range(0, headings.Count).ToList();   // triage failed → read them all
    }

    // The triage system prompt: pick the headings whose section is most likely to carry this node's data.
    private static string TriageSystemFor(ExtractionNode node)
    {
        var target = node switch
        {
            ExtractionNode.COST => "cost, expense, COGS, operating-expense or major-supplier figures",
            ExtractionNode.RISK => "disclosed risk factors or market-risk exposures",
            _                   => "revenue figures or segment / product / region / major-customer revenue breakdowns",
        };
        return "You triage sub-section headings from one US public company's SEC filing. You are given a " +
               "numbered list of headings as 'id: [Item] Title'. Choose the ids of the headings whose " +
               "section most likely contains " + target + ". Prefer headings that name specifics over " +
               "generic or boilerplate ones; skip headings clearly unrelated. Return only ids from the " +
               "list. Reply with JSON only, no prose, no code fences: {\"ids\":[0,3,7]}.";
    }

    // Pull the chosen heading ids out of the triage JSON, keeping only valid, distinct, in-range ids.
    private static List<int> ParseIds(string answer, int count)
    {
        using var doc = LlmJson.ParseObject(answer);
        if (doc is null || !doc.RootElement.TryGetProperty("ids", out var ids) || ids.ValueKind != JsonValueKind.Array)
            return [];
        var seen = new HashSet<int>();
        foreach (var el in ids.EnumerateArray())
        {
            int? id = el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n) ? n
                    : el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var s) ? s
                    : null;
            if (id is { } v && v >= 0 && v < count) seen.Add(v);
        }
        return seen.ToList();
    }

    private async Task<List<FilingHeading>> GetOrParseHeadingsAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType, CancellationToken ct)
    {
        if (_cache.TryGetValue(HeadingsKey(accession, doc, node), out List<FilingHeading>? cached) && cached is not null)
            return cached;
        var raw = await FetchRawAsync(companyId, accession, doc, filingType, ct);
        var headings = raw is null ? [] : FilingSections.BuildHeadings(raw, FilingSections.ItemsFor(node));
        _cache.Set(HeadingsKey(accession, doc, node), headings, CacheFor);
        return headings;
    }

    private static string RawKey(string accession, string doc) => $"filing-raw:{accession}:{doc}";

    // Fetch the filing as clean markdown via the sec2md sidecar (so headings/triage read semantic
    // titles); fall back to the raw SEC HTML when the sidecar is down — FilingSections reads both.
    // Cached per filing so a single scan (headings + sequential Item 8) converts the document once.
    private async Task<string?> FetchRawAsync(
        long companyId, string accession, string doc, string? filingType, CancellationToken ct)
    {
        if (_cache.TryGetValue(RawKey(accession, doc), out string? cached)) return cached;

        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return null;
        var cik = Cik.Trim(company.Cik);
        var acc = accession.Replace("-", "");

        var md = await _sec2md.ToMarkdownAsync(cik, acc, doc, filingType, ct);
        var result = !string.IsNullOrWhiteSpace(md) ? md : await _client.GetFilingDocument(cik, acc, doc);
        if (string.IsNullOrWhiteSpace(result)) return null;
        _cache.Set(RawKey(accession, doc), result, CacheFor);
        return result;
    }

    // Pack consecutive picked headings that share an Item into one worker call, up to the chunk budget.
    // Each packed chunk keeps the titles it bundled (for the widget) and prefixes every title as a
    // markdown header inside the text so the worker still sees the sub-section boundaries.
    private static List<FilingChunk> PackHeadings(IReadOnlyList<FilingHeading> picked)
    {
        var chunks = new List<FilingChunk>();
        string? item = null;
        var titles = new List<string>();
        var sb = new StringBuilder();
        void Flush()
        {
            if (sb.Length == 0) return;
            chunks.Add(new FilingChunk(item!, sb.ToString(), item!, titles));
            sb = new StringBuilder();
            titles = new List<string>();
        }
        foreach (var h in picked)
        {
            var piece = $"## {h.Title}\n{h.Body}";
            // Start a new chunk when the Item changes (keep section grouping clean) or the budget is hit.
            if (sb.Length > 0 && (h.Section != item || sb.Length + piece.Length > FilingSections.MaxChunkChars))
                Flush();
            item = h.Section;
            if (sb.Length > 0) sb.Append("\n\n");
            sb.Append(piece);
            titles.Add(h.Title);
        }
        Flush();
        return chunks;
    }

    // Map/reduce over a given set of chunks: parallel Flash workers, then dedupe by name (first wins).
    private async Task<List<ExtractionSuggestion>> ScanChunksAsync(
        IReadOnlyList<FilingChunk> chunks, ExtractionNode node, Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallel);
        var perChunk = await Task.WhenAll(chunks.Select((c, i) => ScanChunkAsync(c, i, node, gate, onProgress, ct)));

        var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in perChunk)
            foreach (var s in list)
                if (!string.IsNullOrWhiteSpace(s.Name) && !byName.ContainsKey(s.Name))
                    byName[s.Name] = s;
        return byName.Values.ToList();
    }

    // Compact, model-readable digest of the workers' candidates (names/values + verbatim proof), so
    // the Pro chat agent can cite them and fill ```save``` blocks without re-reading the filing.
    private static string FormatDigest(IReadOnlyList<ExtractionSuggestion> findings, ExtractionNode node)
    {
        var label = node switch
        {
            ExtractionNode.COST => "cost candidates",
            ExtractionNode.RISK => "risk candidates",
            _                   => "revenue candidates",
        };
        var sb = new StringBuilder(
            $"PARALLEL-SCAN FINDINGS ({label} the worker agents pulled from the filing):\n");
        foreach (var s in findings)
        {
            sb.Append("- ").Append(s.Name);
            if (s.Classification != null) sb.Append(" [").Append(s.Classification).Append(']');
            if (s.Value != null) sb.Append(" | value=").Append(s.Value);
            if (s.Percentage != null) sb.Append(" | pct=").Append(s.Percentage);
            if (!string.IsNullOrWhiteSpace(s.RelatedCompany)) sb.Append(" | counterparty=").Append(s.RelatedCompany);
            if (!string.IsNullOrWhiteSpace(s.Note)) sb.Append(" | note=").Append(s.Note);
            sb.Append(" | from ").Append(s.Section).Append('\n');
            AppendProof(sb, "name", s.Proof.Name);
            AppendProof(sb, "value", s.Proof.Value);
            AppendProof(sb, "percentage", s.Proof.Percentage);
            AppendProof(sb, "classification", s.Proof.Classification);
            AppendProof(sb, "related_company", s.Proof.RelatedCompany);
            AppendProof(sb, "note", s.Proof.Note);
        }
        return sb.ToString();
    }

    private static void AppendProof(StringBuilder sb, string field, string? proof)
    {
        if (!string.IsNullOrWhiteSpace(proof))
            sb.Append("    proof.").Append(field).Append(": \"").Append(proof).Append("\"\n");
    }

    // One worker: read a single chunk under the concurrency gate and return its candidates. Reports
    // Running once it acquires a slot (so the widget shows queued→running as the pool drains) and
    // Done/Error with the candidate count when it finishes.
    private async Task<List<ExtractionSuggestion>> ScanChunkAsync(
        FilingChunk chunk, int index, ExtractionNode node, SemaphoreSlim gate,
        Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        onProgress?.Invoke(new ScanProgress(ScanChunkPhase.Running, index, 0, null));
        var system = SystemFor(node);
        var prompt = $"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";
        // The full transcript the worker saw — both halves, so the widget's inspector shows exactly
        // what was sent, not just the excerpt.
        var transcript = $"━━ SYSTEM PROMPT ━━\n{system}\n\n━━ USER PROMPT ━━\n{prompt}";
        try
        {
            // A packed chunk can carry many sources, and each echoes its backing text verbatim in
            // 'proof', so 1500 truncated dense sections mid-array. 4000 fits the typical chunk; the
            // salvage in Parse still recovers complete sources if a worst-case chunk overruns even this.
            var answer = await _llm.CompleteAsync(system, prompt, maxTokens: 4000, jsonObject: true, fast: true, ct: ct);
            var found = Parse(answer, chunk.Section).ToList();
            onProgress?.Invoke(new ScanProgress(ScanChunkPhase.Done, index, found.Count, null, transcript, answer));
            return found;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            onProgress?.Invoke(new ScanProgress(ScanChunkPhase.Error, index, 0, null, transcript, ex.Message));
            return [];   // a dropped worker shouldn't sink the whole scan
        }
        finally { gate.Release(); }
    }

    // Pull suggestions out of the model's JSON, tolerant of code fences and string-or-number values.
    // Salvage a truncated reply (finish_reason=length): the sources array was cut mid-stream so the
    // outer structure never closed — `]}` recovers every complete source up to the last closing brace,
    // dropping only the half-written trailing object (instead of voiding the whole chunk → "0 matches").
    private static IEnumerable<ExtractionSuggestion> Parse(string answer, string section)
    {
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null ||
            !doc.RootElement.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in sources.EnumerateArray())
        {
            var name = Str(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            var proof = el.TryGetProperty("proof", out var p) ? p : default;
            yield return new ExtractionSuggestion(
                Name: name!,
                Classification: Str(el, "classification"),
                Value: LlmJson.Num(el, "value"),
                Percentage: LlmJson.Num(el, "percentage"),
                RelatedCompany: Str(el, "related_company"),
                Section: section,
                Proof: new ExtractionProof(
                    Str(proof, "name"), Str(proof, "value"), Str(proof, "percentage"),
                    Str(proof, "classification"), Str(proof, "related_company"), Str(proof, "note")),
                Note: Str(el, "note"));
        }
    }

    // Local to this service: unlike LlmJson.Str, it surfaces JSON numbers as their string form (proof
    // substrings and value cells can arrive as numbers) and keeps a literal "null" verbatim.
    private static string? Str(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }
}
