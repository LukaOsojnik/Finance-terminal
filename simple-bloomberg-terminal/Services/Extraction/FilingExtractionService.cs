using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

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
    private readonly IFilingReportReader _reports;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    private const int MaxParallel = 6;   // concurrent worker calls in the map phase
    private const int WorkerMaxTokens = 16_000;
    private const int WorkerRetryMaxTokens = 32_000;

    // Below this many headings, an Item's outline is treated as undetected rather than short — see
    // the `thin` handling in ScanAutoAsync. Real MD&A outlines run to dozens of sub-headings.
    private const int MinHeadingsPerItem = 5;

    // A thin Item gets at least this many chunks even when the other feeds have already spent the
    // scan's ceiling. Reading a whole Item in six ranked chunks is thin coverage, but it is coverage;
    // dropping to zero would silently remove an entire routed Item from the scan.
    private const int MinChunksPerThinItem = 6;

    public FilingExtractionService(
        ICompanyRepository companies, IStockApiClient client, IFilingReportReader reports,
        IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _reports = reports;
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
    //
    // 'evidence' is the FIRST key of every source, and deliberately so. A model writes a JSON object
    // left to right, so with the quote last it commits to a figure and only then reaches for something
    // to justify it — post-hoc rationalising, structurally unable to correct the answer. Quoting first
    // forces it to locate the row label, the units and the column header BEFORE it can emit a number,
    // which is the same work as reading the column correctly. It is also the only evidence the Pro
    // chat agent ever sees: FormatDigest hands it these strings instead of re-reading the filing, so
    // a thin quote leaves it nothing to disagree with.
    //
    // It is ONE string, not the per-field object this used to be. The model's own output showed the
    // per-field split was fiction: proof.name and proof.value came back as the same sentence whenever
    // a source had a figure, and proof.classification was always a torn-off fragment, because a
    // classification is a judgement and there is nothing in the filing to quote for it. A schema that
    // demands impossible data gets fabricated data. Note that the quote-first discipline above comes
    // from evidence being FIRST, not from it being split — so none of it is lost.
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
            "named supplier/counterparty if the row is about one, else null). Write 'evidence' FIRST, " +
            "before the fields it backs: ONE VERBATIM substring of this excerpt that backs this cost, " +
            "then fill the fields to match what you quoted. Quote enough to identify every figure you " +
            "report — for a table row that means its row label, its units and its column header. Reply " +
            "with JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"evidence\":\"\",\"name\":\"\",\"classification\":\"\"," +
            "\"value\":null,\"percentage\":null,\"related_company\":null}]}. If the excerpt names no " +
            "cost source, reply {\"sources\":[]}.",

        ExtractionNode.RISK =>
            "You extract RISKS a single US public company discloses, from one excerpt of its SEC " +
            "filing (Item 1A risk factors / Item 7A market risk). Return ONLY risks clearly evidenced " +
            "in THIS excerpt — do not guess or carry over outside knowledge. For each risk provide: " +
            "name (a short label for the risk), classification (its scope, exactly one of " +
            "MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL), note (one or " +
            "two sentences summarising the risk in plain language). Write 'evidence' FIRST, before the " +
            "fields it backs: ONE VERBATIM substring of this excerpt that backs this risk, then fill " +
            "the fields to match what you quoted. Reply with JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"evidence\":\"\",\"name\":\"\",\"classification\":\"\",\"note\":null}]}. " +
            "If the excerpt names no risk, reply {\"sources\":[]}.",

        // The "A NAMED COUNTERPARTY IS ITSELF A SOURCE" block is what makes routing Items 1/1A pay
        // off. Without it every field in this prompt points at a figure, and SourceType offers no
        // bucket for a partner — so a worker reading AMD's Item 1A dropped "We are finalizing an
        // investment and partnership agreement with OpenAI" on the floor: no dollar amount, no
        // segment label, nothing it was licensed to return. The counterparty is modelled by
        // related_company (RevenueSource.RelatedCompanyId, nullable), not by a new SourceType value.
        //
        // The exclusion list is load-bearing in the other direction. Risk factors name companies
        // constantly — competitors, plaintiffs, vendors — and without it Item 1A would return every
        // proper noun on the page.
        _ =>
            "You extract revenue sources for a single US public company from one excerpt of its SEC " +
            "filing. Return ONLY the sources clearly evidenced in THIS excerpt — do not guess or carry " +
            "over outside knowledge. Focus on the revenue LABEL and its breakdown — segment, product, " +
            "region or major customer; the exact company-total dollar figures are sourced separately " +
            "from tagged XBRL, so prioritise getting the name/segment/customer and proof right over " +
            "transcribing big totals. A NAMED COUNTERPARTY IS ITSELF A SOURCE, even when the excerpt " +
            "states no figure for it: return customers, commercial partners, joint-venture and " +
            "equity-method counterparties, distributors and resellers — set name AND related_company " +
            "to that company, classification to CUSTOMER, and leave value and percentage null. Return " +
            "it even when the relationship is described as pending, proposed, being finalised or not " +
            "yet signed; quote the hedging words verbatim in proof so the reviewer sees them. Do NOT " +
            "return a company named only as a competitor, a litigation adversary, a supplier or " +
            "vendor, or an acquisition target — those are not revenue counterparties. For each source " +
            "provide: name (the segment / product / region / " +
            "major-customer label), classification (exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT), " +
            "value (revenue in absolute US dollars — scale any 'in thousands/millions' to the full " +
            "number; null if not stated), percentage (share of total revenue 0-100, null if not stated), " +
            "related_company (a named counterparty/customer if the row is about one, else null). Write " +
            "'evidence' FIRST, before the fields it backs: ONE VERBATIM substring of this excerpt that " +
            "backs this source, then fill the fields to match what you quoted. Quote enough to identify " +
            "every figure you report — for a table row that means its row label, its units and its " +
            "column header, and for a percentage the words that say what it is a share OF. Reply with " +
            "JSON only, no prose, no code fences: " +
            "{\"sources\":[{\"evidence\":\"\",\"name\":\"\",\"classification\":\"\"," +
            "\"value\":null,\"percentage\":null,\"related_company\":null}]}. If the excerpt names no " +
            "revenue source, reply {\"sources\":[]}.",
    };

    public async Task<IReadOnlyList<ExtractionSuggestion>> ExtractAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        CancellationToken ct = default)
    {
        var raw = await FetchRawAsync(companyId, accession, doc, filingType, ct);
        if (raw is null) return [];
        return await ScanChunksAsync(
            FilingSections.Build(raw, FilingSections.ItemsFor(node, filingType)), node, null, ct);
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
        var items = FilingSections.ItemsFor(node, filingType);

        // Heading triage drives the narrative items (e.g. Item 7 MD&A), where bold headings line up
        // with topic boundaries. Item 8 (financial statements) is handled separately, below.
        var picked = headings.Count > 0 ? await TriageHeadingsAsync(headings, node, ct) : [];
        var pickedSet = picked.ToHashSet();
        // Always scan all of Item 1 (Business) and Item 7 (MD&A): for revenue/cost we want the full
        // business, supplier and segment narrative, which triage can skip when judging by title.
        // No-op for RISK (its sections are 1A/7A) and for 8-K Item 1.01 (the match is exact).
        for (int i = 0; i < headings.Count; i++)
            if (headings[i].Section is "Item 1" or "Item 7") pickedSet.Add(i);

        // Heading-based chunks for the picked headings — but NOT Item 8. In the financial statements the
        // tables are detached from their bold headings, so "nearest heading" mislabels them (a segment
        // revenue table lands under a tax note) and the per-heading cap truncates them.
        // Pack consecutive same-Item headings into one worker call up to the chunk budget: a tiny heading
        // body no longer wastes a whole LLM call — several small titles ride together, fewer calls.
        // Items whose heading outline is too thin to be a real outline. Bold-detection assumes filers
        // mark sub-headings with font-weight; Intel styles them by size and colour instead, so its
        // 3.3 MB 10-K surfaces 4 headings where AMD's surfaces 86 — the heading path would hand the
        // workers two thin chunks and silently miss the MD&A segment tables. Read those Items
        // sequentially instead: the same treatment Item 8 gets, for the same reason.
        var thin = items
            .Where(i => i != "8" && headings.Count(h => h.Section == $"Item {i}") < MinHeadingsPerItem)
            .ToHashSet();

        var pickedHeadings = pickedSet
            .OrderBy(i => i)
            .Select(i => headings[i])
            .Where(h => h.Section != "Item 8" && !thin.Contains(h.Section["Item ".Length..]))
            .ToList();
        var chunks = PackHeadings(pickedHeadings);

        // Item 8: the SEC's rendered statement reports, one clean table per file. Falls back to
        // sequential document-order chunks of the section when the filing has no report index, so
        // every table still reaches a worker intact and in place.
        if (items.Contains("8"))
        {
            var reportChunks = await ReportChunksAsync(companyId, accession, node, ct);
            if (reportChunks.Count > 0)
                chunks.AddRange(reportChunks);
            else if (await FetchRawAsync(companyId, accession, doc, filingType, ct) is { } raw)
                chunks.AddRange(FilingSections.BuildSection(raw, "8", node));
        }

        // The thin Items absorb whatever the other two feeds left of the scan's ceiling. They go last
        // for a reason: Item 8 carries the audited tables and the triaged headings were chosen on
        // purpose, whereas this feed is a blind sequential read of a section whose outline we failed
        // to parse. When something has to give, it should give here.
        //
        // Before this, the three feeds each sized themselves independently and nothing reconciled
        // them: three thin Items meant 3 × BuildSection's 40 = 120 untriaged calls on top of Item 8,
        // i.e. the filings with the worst targeting also cost the most. The floor keeps a thin Item
        // from being squeezed to nothing when the ceiling is already spent.
        if (thin.Count > 0 && await FetchRawAsync(companyId, accession, doc, filingType, ct) is { } thinRaw)
        {
            var remaining = Math.Max(0, FilingSections.MaxScanChunks - chunks.Count);
            var perItem = Math.Max(MinChunksPerThinItem, remaining / thin.Count);
            foreach (var item in thin)
                chunks.AddRange(FilingSections.BuildSection(thinRaw, item, node, perItem));
        }

        // The page's triage report: every heading offered + whether scanned. Item 8 and the thin Items
        // are read in full sequentially, so their headings are all marked scanned.
        var report = headings
            .Select((h, i) => new ScannedHeading(h.Section, h.Title,
                h.Section == "Item 8" || thin.Contains(h.Section["Item ".Length..]) || pickedSet.Contains(i)))
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
            // Same reasoning-token trap as ScanChunkAsync: the visible answer is a short list of ids,
            // but on a reasoning fast model this ceiling is max_completion_tokens and covers the
            // reasoning too — and triage reasons over the WHOLE heading list, which runs to 86 titles
            // on AMD. 800 was not a budget for that, and the failure was invisible: an empty parse
            // falls through to "read them all" below, quietly turning triage off and scanning every
            // heading. That reads as a slow, expensive scan, never as an error.
            var answer = await _llm.CompleteAsync(
                TriageSystemFor(node), $"Headings:\n{list}", maxTokens: 8000, jsonObject: true, fast: true, ct: ct);
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
            // Named after the disclosures that actually carry the breakdowns, so triage recognises them
            // by their own heading wording rather than only by the word "revenue": segment and
            // geographic revenue (ASC 280), disaggregation of revenue and remaining performance
            // obligations (ASC 606), major-customer concentration (ASC 275-10-50), equity-method
            // investments and JV partners (ASC 323), plus the Item 1 / 8-K narrative of customers,
            // distribution channels, backlog and named contract wins.
            //
            // The trailing risk-factor clause is what makes Item 1A affordable. Its headings are 20-40
            // pages of largely generic risk, of which only the concentration ones bear on revenue, and
            // unlike Item 7 it is NOT force-included below — triage is the only thing filtering it. So
            // the qualifier ("that name a specific customer…") is doing real work: without it triage
            // reads a section title like "Risks Related to Our Business" as on-topic and takes the lot.
            _                   => "revenue figures or revenue breakdowns — by segment, product, " +
                                   "region or major customer; disaggregation of revenue, remaining " +
                                   "performance obligations, customer concentration, equity-method " +
                                   "investments and joint-venture partners; the narrative on " +
                                   "customers, distribution channels, backlog and named contracts; " +
                                   "and risk factors that name a specific customer, contract, " +
                                   "backlog or revenue concentration — but NOT generic risk " +
                                   "boilerplate that names no counterparty and gives no figure",
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
        var headings = raw is null
            ? []
            : FilingSections.BuildHeadings(raw, FilingSections.ItemsFor(node, filingType));
        _cache.Set(HeadingsKey(accession, doc, node), headings, CacheFor);
        return headings;
    }

    // Public so the evidence viewer's document endpoint (FilingsController.Document) reads the very
    // entry a scan already populated: one cached copy of a multi-MB filing, and an instant open when
    // the user clicks a quote right after the scan that produced it.
    public static string RawKey(string accession, string doc) => $"filing-raw:{accession}:{doc}";

    // Fetch the filing document straight from EDGAR as HTML. FilingSections parses that directly now —
    // the Python sec2md sidecar that used to sit here converted the filing to markdown, which cost us
    // every table's column structure on the way in (see docs/sec-extraction.md). Cached per filing so
    // one scan (headings + Item 8) fetches the document once.
    private async Task<string?> FetchRawAsync(
        long companyId, string accession, string doc, string? filingType, CancellationToken ct)
    {
        if (_cache.TryGetValue(RawKey(accession, doc), out string? cached)) return cached;
        if (CompanyCik(companyId) is not { } cik) return null;

        var result = await _client.GetFilingDocument(cik, accession.Replace("-", ""), doc);
        if (string.IsNullOrWhiteSpace(result)) return null;
        _cache.Set(RawKey(accession, doc), result, CacheFor);
        return result;
    }

    // The company's EDGAR CIK in the trimmed form the Archives paths use, or null when it has none.
    private string? CompanyCik(long companyId)
    {
        var company = _companies.GetById(companyId);
        return company is null || string.IsNullOrWhiteSpace(company.Cik) ? null : Cik.Trim(company.Cik);
    }

    private static string ReportsKey(string accession, ExtractionNode node) =>
        $"filing-reports:{node}:{accession}";

    // Item 8 comes from the SEC's own rendered reports (R*.htm) rather than from the filing document:
    // they are the financial statements already reconstructed as well-formed tables, one per file,
    // with units in the title and the us-gaap concept on every label cell. Best-effort — a filing with
    // no FilingSummary.xml (pre-2009) yields nothing and the caller falls back to the document.
    private async Task<List<FilingChunk>> ReportChunksAsync(
        long companyId, string accession, ExtractionNode node, CancellationToken ct)
    {
        if (_cache.TryGetValue(ReportsKey(accession, node), out List<FilingChunk>? cached) && cached is not null)
            return cached;
        if (CompanyCik(companyId) is not { } cik) return [];

        List<FilingChunk> chunks;
        try
        {
            var reports = await _reports.ReportsAsync(
                cik, accession.Replace("-", ""), e => FilingSections.SelectReport(e, node), ct);
            chunks = FilingSections.BuildReports(reports);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            chunks = [];
        }
        _cache.Set(ReportsKey(accession, node), chunks, CacheFor);
        return chunks;
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

    // Map/reduce over a given set of chunks: parallel Flash workers, then combine by name.
    private async Task<List<ExtractionSuggestion>> ScanChunksAsync(
        IReadOnlyList<FilingChunk> chunks, ExtractionNode node, Action<ScanProgress>? onProgress, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallel);
        var perChunk = await Task.WhenAll(chunks.Select((c, i) => ScanChunkAsync(c, i, node, gate, onProgress, ct)));

        var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in perChunk)
            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) continue;
                byName[s.Name] = byName.TryGetValue(s.Name, out var seen) ? Combine(seen, s) : s;
            }
        return byName.Values.ToList();
    }

    /// <summary>
    /// Two chunks naming the same source are two partial views of it, not a duplicate to discard.
    /// Document order runs the wrong way for "first wins": a filing's Overview NAMES the segments and
    /// the section below it carries their figures, so keeping the first sighting whole threw away
    /// every number the filing actually stated — 3M's Item 7 overview alone was enough to shadow
    /// $11,384M and 45.6% for Safety and Industrial.
    ///
    /// <paramref name="a"/>'s Section is kept: it is where the source was first evidenced.
    ///
    /// Evidence is one string per source now, so unlike the old per-field proof a merge has to CHOOSE
    /// which quote survives. It keeps the quote belonging to whichever record supplied the VALUE: the
    /// figure is the field most likely to be misread, so it is the one whose backing must be the
    /// quote a reviewer sees. When neither side states a figure, either quote describes the same
    /// source and the first is as good as the second.
    /// </summary>
    private static ExtractionSuggestion Combine(ExtractionSuggestion a, ExtractionSuggestion b)
    {
        var cls = a.Classification ?? b.Classification;
        var value = a.Value ?? b.Value;
        var pct = a.Percentage ?? b.Percentage;
        var related = !string.IsNullOrWhiteSpace(a.RelatedCompany) ? a.RelatedCompany : b.RelatedCompany;
        var note = !string.IsNullOrWhiteSpace(a.Note) ? a.Note : b.Note;

        var figure = a.Value is not null ? a : b.Value is not null ? b : a;
        var evidence = !string.IsNullOrWhiteSpace(figure.Evidence) ? figure.Evidence
            : !string.IsNullOrWhiteSpace(a.Evidence) ? a.Evidence : b.Evidence;

        return a with
        {
            Classification = cls,
            Value = value,
            Percentage = pct,
            RelatedCompany = related,
            Note = note,
            Evidence = evidence,
        };
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
            if (!string.IsNullOrWhiteSpace(s.Evidence))
                sb.Append("    evidence: \"").Append(s.Evidence).Append("\"\n");
        }
        return sb.ToString();
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
            // 'proof', so 1500 truncated dense sections mid-array.
            //
            // The ceiling is deliberately far above what the visible JSON needs, because on a
            // REASONING fast model (OpenAI's tier here is gpt-5-mini) this cap is sent as
            // max_completion_tokens, which covers the model's reasoning tokens AND the reply. At 4000
            // reasoning consumed nearly the whole budget and the reply died 15 tokens in, mid-proof:
            //   {"sources":[{"proof":{"name":"sales in EMEA","value":null
            // Raising it is close to free — it is a ceiling, not a spend, and unused tokens are never
            // billed — whereas setting it too low silently returns zero findings.
            var completion = await _llm.CompleteDetailedAsync(
                system, prompt, maxTokens: WorkerMaxTokens, jsonObject: true, fast: true, ct: ct);
            var answer = completion.Content;
            var found = Parse(answer, chunk.Section).ToList();

            // Retry exactly once when the provider confirms that generation hit the ceiling before
            // producing any usable JSON. Other malformed replies are not token-budget failures, and
            // a truncated reply from which Parse salvaged complete sources is already useful.
            var retried = false;
            if (found.Count == 0 && !IsJsonObject(answer) &&
                string.Equals(completion.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                retried = true;
                completion = await _llm.CompleteDetailedAsync(
                    system, prompt, maxTokens: WorkerRetryMaxTokens,
                    jsonObject: true, fast: true, ct: ct);
                answer = completion.Content;
                found = Parse(answer, chunk.Section).ToList();
            }

            // A reply that does not parse as JSON AT ALL is a failed call, not an honest "no sources
            // in this excerpt" — and the two are indistinguishable downstream, since both end as an
            // empty list. Report the provider's actual finish reason alongside the raw reply rather
            // than guessing that every malformed response hit the token ceiling.
            if (!IsJsonObject(answer) && found.Count == 0)
            {
                var finish = completion.FinishReason is { Length: > 0 } reason
                    ? $"finish_reason={reason}"
                    : "finish_reason unavailable";
                var retry = retried
                    ? $" Retry with maxTokens={WorkerRetryMaxTokens} also failed."
                    : "";
                onProgress?.Invoke(new ScanProgress(ScanChunkPhase.Error, index, 0, null, transcript,
                    $"Reply was not valid JSON ({finish}).{retry} Raw reply:\n{answer}"));
                return found;
            }

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

    private static bool IsJsonObject(string answer)
    {
        using var probe = LlmJson.ParseObject(answer);
        return probe is not null;
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
            yield return new ExtractionSuggestion(
                Name: name!,
                Classification: Str(el, "classification"),
                Value: LlmJson.Num(el, "value"),
                Percentage: LlmJson.Num(el, "percentage"),
                RelatedCompany: Str(el, "related_company"),
                Section: section,
                Evidence: Str(el, "evidence"),
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
