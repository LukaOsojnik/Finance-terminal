// OBJEDINJENI PREGLED PROGRAMSKIH DATOTEKA ZA FILING-EKSTRAKCIJU
//
// Dokument je referentna kopija izvornog koda za potrebe dokumentacije.
// Ne ukljucivati ga u projekt: sadrzi kopije postojecih tipova i vise file-scoped namespace deklaracija.
// Izvorne datoteke navedene su u naslovu svakog odjeljka.

#if false

#region FilingSections
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/FilingSections.cs
// NAMJENA: Parsira primarni SEC dokument, prepoznaje relevantne SEC stavke i naslove te tekst dijeli u ogranicene dijelove za LLM radnike.
// ============================================================================

using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// One filing slice sent to a worker. Section identifies its source, Item groups it in the widget,
// and Titles lists any sub-headings bundled into the call.
public record FilingChunk(string Section, string Text, string Item = "", IReadOnlyList<string>? Titles = null);

// A bold sub-heading within a target Item and its body text.
public record FilingHeading(string Section, string Title, string Body);

// Converts raw SEC filings into targeted, size-limited plain-text extraction chunks.
public static class FilingSections
{
    // Maps each node to its relevant annual-report SEC Items, ordered so high-priority sections receive
    // chunk capacity first.
    public static string[] ItemsFor(ExtractionNode node) => node switch
    {
        ExtractionNode.RISK => ["1A", "7A"],
        // COST includes Item 1 because supplier and raw-material disclosures may not appear in the notes.
        ExtractionNode.COST => ["1", "7", "8"],
        // Revenue spans Items 1, 1A, 7, and 8 because named customers and commercial relationships
        // can appear in the business, concentration-risk, MD&A, and financial-note narratives.
        _ => ["1", "1A", "7", "8"],
    };

    public const int MaxChunkChars = 4000;         // ~1k tokens/chunk — the per-worker text budget
    private const int MaxChunksPerSection = 12;    // keep one giant section from hogging every slot
    // Caps worker calls so later high-value Items are not starved and malformed filings cannot create
    // unbounded scans.
    public const int MaxScanChunks = 48;

    public static List<FilingChunk> Build(string raw, string[] items)
    {
        var text = ToText(raw);
        var chunks = new List<FilingChunk>();
        foreach (var item in items)
        {
            var body = SectionBody(text, item);
            if (body is null) continue;
            int n = 0;
            foreach (var chunk in Paragraphs(body))
            {
                chunks.Add(new FilingChunk($"Item {item}", chunk, $"Item {item}"));
                if (++n >= MaxChunksPerSection) break;          // fair share to the next section
                if (chunks.Count >= MaxScanChunks) return chunks;
            }
        }
        return chunks;
    }

    // Builds sequential chunks when headings are unreliable. Ranks excess chunks because important
    // financial tables often appear late in an Item.
    public static List<FilingChunk> BuildSection(string raw, string item, ExtractionNode node, int maxChunks = 40)
    {
        var body = SectionBody(ToText(raw), item);
        if (body is null) return [];
        var chunks = Paragraphs(body)
            .Select(text => new FilingChunk($"Item {item}", text, $"Item {item}"))
            .ToList();
        return RankChunks(chunks, node, maxChunks);
    }

    // Prefer table chunks over keyword-heavy prose because extraction targets usually live in tables.

    // Keeps the most relevant chunks, then restores document order. Deterministic scoring avoids an
    // extra model call and keeps plans testable.
    public static List<FilingChunk> RankChunks(IReadOnlyList<FilingChunk> chunks, ExtractionNode node, int take)
    {
        if (chunks.Count <= take) return chunks.ToList();
        return chunks
            .Select((chunk, index) => (chunk, index, score: Relevance(chunk.Text, node)))
            .OrderByDescending(x => x.score)
            .ThenBy(x => x.index)        // ties fall back to document order, so ranking is stable
            .Take(take)
            .OrderBy(x => x.index)       // ...but the workers still read the survivors in filing order
            .Select(x => x.chunk)
            .ToList();
    }

    // Distinct keyword hits (not occurrences — a paragraph repeating "segment" ten times is not ten
    // times as relevant as one naming both a segment and a customer), plus the table bonus.
    private static int Relevance(string text, ExtractionNode node)
    {
        var score = 0;
        foreach (var keyword in Keywords(node))
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score++;
        return score;
    }

    // Shared topic words used to rank chunks and select node-relevant detail reports.
    private static string[] Keywords(ExtractionNode node) => node switch
    {
        // Omit "risk" because it matches all of Item 1A; specific exposure terms provide useful ranking.
        ExtractionNode.RISK =>
            ["Concentration", "Depend", "Single Source", "Sole Source", "Litigation", "Regulat",
             "Interest Rate", "Foreign Currency", "Exchange Rate", "Cyber", "Supply", "Tariff"],
        ExtractionNode.COST =>
            ["Supplier", "Vendor", "Manufactur", "Foundry", "Purchase", "Supply", "License", "Service Provider"],
        _ =>
            ["Customer", "Buyer", "Distributor", "Reseller", "Licensee", "Commercial Partner",
             "Joint Venture", "Concentration", "Depend"],
    };


    // Checks whether the input looks like HTML rather than a plain-text filing.
    private static bool LooksHtml(string raw) =>
        Regex.IsMatch(raw[..Math.Min(raw.Length, 2000)], "<html|<body|<div|<p|<table", RegexOptions.IgnoreCase);
    // Converts HTML to readable text without interpreting table rows or columns. Cell boundaries get
    // whitespace only so layout-table prose remains readable while financial grids receive no special treatment.
    private static string ToText(string raw)
    {
        if (LooksHtml(raw))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

            // Preserve ordinary visual boundaries, but do not reconstruct or classify tables.
            var marked = Regex.Replace(doc.DocumentNode.OuterHtml, "(?i)</(td|th)>", " ");
            marked = Regex.Replace(marked, "(?i)</(p|div|tr|li|h[1-6]|table)>", "\n");
            marked = Regex.Replace(marked, "(?i)<br\\s*/?>", "\n");
            var flat = new HtmlDocument();
            flat.LoadHtml(marked);
            raw = HtmlEntity.DeEntitize(flat.DocumentNode.InnerText) ?? "";
        }

        var lines = raw.Replace("\r", "").Split('\n')
            .Select(line => Regex.Replace(line, "[ \t ]+", " ").Trim());
        var sb = new StringBuilder();
        var blanks = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0)
            {
                blanks++;
                if (blanks <= 1) sb.Append('\n');
                continue;
            }
            blanks = 0;
            sb.Append(line).Append('\n');
        }
        return sb.ToString();
    }

    // Maps canonical Reg S-K titles to Items because body headings may omit Item numbers. Item 1 is
    // excluded because the generic title "Business" creates too many false matches.
    private static readonly (string Num, string Title)[] ItemTitles =
    [
        ("1A", @"Risk\s+Factors"),
        ("7A", @"Quantitative\s+and\s+Qualitative\s+Disclosures?\s+About\s+Market\s+Risk"),
        ("7",  @"Management'?.?s\s+Discussion\s+and\s+Analysis(\s+of\s+Financial\s+Condition.*)?"),
        ("8",  @"Financial\s+Statements\s+and\s+Supplementary\s+Data"),
    ];

    // Matches an Item heading at the start of a line, including decimal 8-K numbers such as 2.02.
    private const string ItemHeadingPattern = @"^[#>*_\s]*Item\s+(\d+(?:\.\d+)?[A-Z]?)\b";

    // Returns the Item headed by this line. Title matches must occupy the full line so inline references
    // are not treated as section boundaries.
    private static string? ItemNumberOf(string line)
    {
        var m = Regex.Match(line, ItemHeadingPattern, RegexOptions.IgnoreCase);
        if (m.Success) return m.Groups[1].Value.ToUpperInvariant();

        foreach (var (num, title) in ItemTitles)
            if (Regex.IsMatch(line, $@"^[#>*_\s]*{title}[.:\s]*$", RegexOptions.IgnoreCase))
                return num;
        return null;
    }

    // Every Item boundary in the document, in order — by number and by canonical title.
    private static List<(string Num, int Start, int End)> Boundaries(string text)
    {
        var found = new List<(string Num, int Start, int End)>();

        foreach (Match m in Regex.Matches(text, $"(?im){ItemHeadingPattern}"))
            found.Add((m.Groups[1].Value.ToUpperInvariant(), m.Index, m.Index + m.Length));

        foreach (var (num, title) in ItemTitles)
            foreach (Match m in Regex.Matches(text, $@"(?im)^[#>*_\s]*{title}[.:\s]*$"))
                found.Add((num, m.Index, m.Index + m.Length));

        return found.OrderBy(h => h.Start).ToList();
    }

    // Choose the longest occurrence to distinguish the body from TOC entries. Stop only at a different
    // Item because filings may repeat the current title as a running header.
    private static string? SectionBody(string text, string item)
    {
        var headings = Boundaries(text);
        if (headings.Count == 0) return null;

        string? best = null;
        for (int i = 0; i < headings.Count; i++)
        {
            if (headings[i].Num != item) continue;
            var bodyStart = headings[i].End;
            var next = headings.FindIndex(i + 1, h => h.Num != item);
            var bodyEnd = next >= 0 ? headings[next].Start : text.Length;
            var body = text[bodyStart..bodyEnd].Trim();
            if (best is null || body.Length > best.Length) best = body;
        }
        return string.IsNullOrWhiteSpace(best) ? null : best;
    }

    // Packs plain-text paragraphs into bounded chunks. Oversized paragraphs are split instead of truncated.
    private static IEnumerable<string> Paragraphs(string body)
    {
        var paragraphs = Regex.Split(body, "\n\\s*\n")
            .Select(paragraph => paragraph.Trim())
            .Where(paragraph => paragraph.Length > 0);
        var current = new StringBuilder();

        foreach (var paragraph in paragraphs)
        {
            for (var offset = 0; offset < paragraph.Length; offset += MaxChunkChars)
            {
                var length = Math.Min(MaxChunkChars, paragraph.Length - offset);
                var piece = paragraph.Substring(offset, length);
                if (current.Length > 0 && current.Length + 2 + piece.Length > MaxChunkChars)
                {
                    yield return current.ToString();
                    current.Clear();
                }
                if (current.Length > 0) current.Append("\n\n");
                current.Append(piece);
            }
        }

        if (current.Length > 0) yield return current.ToString();
    }

    // ── Heading-level view: bold sub-headings inside Items 7/8/1A + the paragraphs under each ──

    private const int HeadingMaxChars = 400;       // a heading is a full bold line (often a sentence)
    private const int HeadingBodyMaxChars = 6000;  // ~1.5k tokens for the worker that reads it
    private const int MaxHeadings = 120;           // safety cap on how many we surface

    // Tags that start a new visual line; a heading is one such line whose text is entirely bold.
    private static readonly HashSet<string> BlockTags =
        ["p", "div", "li", "tr", "table", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6"];

    // Extracts bold sub-headings and their bodies for focused worker calls. Plain-text filings use the
    // line-based fallback because they lack bold markup.
    public static List<FilingHeading> BuildHeadings(string raw, string[] items)
    {
        // Plain-text filings use line-based headings because they have no bold markup.
        if (!LooksHtml(raw)) return BuildHeadingsFromMarkdown(raw, items);

        var doc = new HtmlDocument();
        doc.LoadHtml(raw);
        doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

        // Flatten the document into visual lines, each tagged with whether its whole text is bold.
        var lines = new List<(string Text, bool Bold)>();
        var acc = new LineAcc();
        CollectLines(doc.DocumentNode, lines, acc);
        FlushLine(lines, acc);

        var result = new List<FilingHeading>();
        string? section = null;            // current Item (null when outside the target items)
        string? title = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (title is not null && section is not null && body.Length > 0)
                result.Add(new FilingHeading(section, title, body.ToString().Trim()));
            title = null;
            body.Clear();
        }

        foreach (var (text, bold) in lines)
        {
            // An Item line — by number or by canonical title — is a section boundary, not a
            // selectable sub-heading.
            if (ItemNumberOf(text) is { } num)
            {
                Flush();
                if (Array.IndexOf(items, num) >= 0)
                {
                    section = $"Item {num}";
                    title = text;   // capture the lead-in before the first sub-heading (e.g. Item 8 tables)
                }
                else section = null;
                continue;
            }

            if (section is null) continue;   // outside the revenue-relevant items

            if (bold && text.Length <= HeadingMaxChars && text.Any(char.IsLetter))
            {
                Flush();
                title = text;
            }
            else if (title is not null && body.Length < HeadingBodyMaxChars)
            {
                body.Append(text).Append('\n');
            }
        }
        Flush();

        // Dedupe (TOC + body can both yield a heading); keep the one with the longer body.
        return result
            .GroupBy(h => $"{h.Section}|{h.Title}")
            .Select(g => g.OrderByDescending(h => h.Body.Length).First())
            .Take(MaxHeadings)
            .ToList();
    }

    // Plain-text heading extraction mirrors the HTML path using Markdown heading markers.
    private static List<FilingHeading> BuildHeadingsFromMarkdown(string raw, string[] items)
    {
        var result = new List<FilingHeading>();
        string? section = null;            // current Item (null when outside the target items)
        string? title = null;
        var body = new StringBuilder();

        void Flush()
        {
            if (title is not null && section is not null && body.Length > 0)
                result.Add(new FilingHeading(section, title, body.ToString().Trim()));
            title = null;
            body.Clear();
        }

        foreach (var rawLine in raw.Replace("\r", "").Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;

            // An Item line — by number or by canonical title — is a section boundary, not a sub-heading.
            if (ItemNumberOf(line) is { } num)
            {
                Flush();
                if (Array.IndexOf(items, num) >= 0)
                {
                    section = $"Item {num}";
                    // Seed the Item title so content before the first sub-heading is retained.
                    title = StripInline(line);
                }
                else section = null;
                continue;
            }

            if (section is null) continue;   // outside the revenue-relevant items

            var heading = MarkdownHeadingText(line);
            if (heading is not null && heading.Length <= HeadingMaxChars && heading.Any(char.IsLetter))
            {
                Flush();
                title = heading;
            }
            else if (title is not null && body.Length < HeadingBodyMaxChars)
            {
                body.Append(line).Append('\n');
            }
        }
        Flush();

        // Dedupe (a TOC and the body can both yield a heading); keep the one with the longer body.
        return result
            .GroupBy(h => $"{h.Section}|{h.Title}")
            .Select(g => g.OrderByDescending(h => h.Body.Length).First())
            .Take(MaxHeadings)
            .ToList();
    }

    // The heading text if this markdown line is a heading — an ATX line ("#…# Title") or a line that
    // is entirely bold ("**Title**") — else null. Inline markers are stripped so triage sees a clean title.
    private static string? MarkdownHeadingText(string line)
    {
        var atx = Regex.Match(line, @"^#{1,6}\s+(.+?)\s*#*$");
        if (atx.Success) return StripInline(atx.Groups[1].Value);

        var bold = Regex.Match(line, @"^\*\*(.+?)\*\*$");
        if (bold.Success && !bold.Groups[1].Value.Contains("**")) return StripInline(bold.Groups[1].Value);

        return null;
    }

    private static string StripInline(string s) => Regex.Replace(s, @"[*_`]", "").Trim();

    private sealed class LineAcc
    {
        public readonly StringBuilder Sb = new();
        public bool AllBold = true;   // ANDed with each text run; a line with one non-bold run isn't a heading
        public bool HasText;
    }

    // Depth-first walk that emits visual lines at block boundaries and line breaks while tracking boldness.
    private static void CollectLines(HtmlNode node, List<(string, bool)> lines, LineAcc acc)
    {
        foreach (var child in node.ChildNodes)
        {
            if (child.NodeType == HtmlNodeType.Text)
            {
                var t = HtmlEntity.DeEntitize(child.InnerText) ?? "";
                if (t.Trim().Length == 0) { acc.Sb.Append(' '); continue; }
                acc.Sb.Append(t);
                acc.AllBold &= HasBoldAncestor(child);
                acc.HasText = true;
            }
            else if (string.Equals(child.Name, "br", StringComparison.OrdinalIgnoreCase))
            {
                FlushLine(lines, acc);
            }
            else if (BlockTags.Contains(child.Name))
            {
                FlushLine(lines, acc);
                CollectLines(child, lines, acc);
                FlushLine(lines, acc);
            }
            else
            {
                CollectLines(child, lines, acc);   // inline element (span, b, font, i, a…)
            }
        }
    }

    private static void FlushLine(List<(string, bool)> lines, LineAcc acc)
    {
        var text = Regex.Replace(acc.Sb.ToString(), "\\s+", " ").Trim();
        if (text.Length > 0 && acc.HasText) lines.Add((text, acc.AllBold));
        acc.Sb.Clear();
        acc.AllBold = true;
        acc.HasText = false;
    }

    private static bool HasBoldAncestor(HtmlNode textNode)
    {
        for (var a = textNode.ParentNode; a is not null; a = a.ParentNode)
        {
            if (a.Name is "b" or "strong") return true;
            var style = a.GetAttributeValue("style", "").ToLowerInvariant();
            if (style.Contains("font-weight") &&
                (style.Contains("bold") || style.Contains("600") || style.Contains("700") ||
                 style.Contains("800") || style.Contains("900")))
                return true;
        }
        return false;
    }
}

#endregion

#region FastWorkerScanService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/FastWorkerScanService.cs
// NAMJENA: Organizira plan skeniranja, paralelno poziva brze LLM radnike, obraduje njihove JSON odgovore i sastavlja digest nalaza.
// ============================================================================

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

public enum FastWorkerChunkPhase { Planned, Running, Done, Error }

// Identifies one filing chunk shown in scan progress.
public record FastWorkerChunkInfo(int Index, string Item, IReadOnlyList<string> Titles);

// Reports the chunk plan, worker state, prompt, and response to the UI or harness.
public record FastWorkerScanProgress(
    FastWorkerChunkPhase Phase, int Index, int Found, IReadOnlyList<FastWorkerChunkInfo>? Plan,
    string? Prompt = null, string? Response = null);

public class FastWorkerScanService : IFastWorkerScanService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    private const int MaxParallelFastWorkers = 6;
    private const int FastWorkerMaxTokens = 16_000;
    private const int FastWorkerRetryMaxTokens = 32_000;
    private const int MinHeadingsPerItem = 5;
    private const int MinChunksPerThinItem = 6;

    public FastWorkerScanService(
        ICompanyRepository companies, IStockApiClient client, IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _llm = llm;
        _cache = cache;
    }

    private static string HeadingsKey(string accession, string doc, ExtractionNode node) =>
        $"filing-headings:{node}:{accession}:{doc}";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    // COST and REVENUE share one directional counterparty contract; RISK has its own compact schema.
    private static string FastWorkerPromptFor(ExtractionNode node, bool strictCounterparties = false) =>
        node == ExtractionNode.RISK
            ? RiskPrompts.FastWorkerSystemPrompt
            : CounterpartyPrompts.FastWorkerSystemPrompt(node, strictCounterparties);

    // Flat fallback: fetches the filing, calls FilingSections to build chunks, then runs workers.
    public async Task<IReadOnlyList<ExtractionSuggestion>> ScanFullSectionsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default)
    {
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        if (raw is null) return [];
        return await RunFastWorkerAgentsAsync(
            FilingSections.Build(raw, FilingSections.ItemsFor(node)), node, null, false, null, ct);
    }

    // Creates fresh lead-agent context from workers. Runs a full-section worker scan only when the
    // targeted scan finds nothing; LLM findings are never read from or written to the cache.
    public async Task<string> CreateFastWorkerDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default)
    {
        var scan = await RunFastWorkerScanAsync(companyId, accession, doc, node, ct: ct);
        if (scan.Found > 0) return scan.FastWorkerDigest;

        var fastWorkerFindings = await ScanFullSectionsAsync(companyId, accession, doc, node, ct);
        return fastWorkerFindings.Count > 0
            ? BuildFastWorkerDigest(fastWorkerFindings, node)
            : "";
    }

    // Main fast-worker scan entry point. Calls parsing, report, chunking, and worker helpers to build one
    // deterministic scan plan and a digest for the lead agent.
    public async Task<FastWorkerScanResult> RunFastWorkerScanAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        Action<FastWorkerScanProgress>? onProgress = null, bool strictCounterparties = false,
        bool captureArtifacts = false,
        CancellationToken ct = default)
    {
        var headings = await GetOrParseHeadingsAsync(companyId, accession, doc, node, ct);
        var items = FilingSections.ItemsFor(node);

        // Sparse heading detection is unreliable, so those Items use full-section chunks instead.
        var thin = items
            .Where(i => i != "8" && headings.Count(h => h.Section == $"Item {i}") < MinHeadingsPerItem)
            .ToHashSet();

        // PackHeadingsIntoChunks reduces calls; RankChunks applies a reproducible relevance budget.
        var pickedHeadings = headings
            .Where(h => h.Section != "Item 8" && !thin.Contains(h.Section["Item ".Length..]))
            .ToList();
        var chunks = FilingSections.RankChunks(
            PackHeadingsIntoChunks(pickedHeadings), node, FilingSections.MaxScanChunks);

        // Item 8 comes from the primary filing like every other Item. We intentionally do not download
        // or interpret the SEC's separately rendered financial-table reports.
        if (items.Contains("8"))
        {
            if (await FetchRawAsync(companyId, accession, doc, ct) is { } raw)
                chunks.AddRange(FilingSections.BuildSection(raw, "8", node));
        }

        // Fill remaining capacity with ranked chunks from Items whose headings were unreliable.
        if (thin.Count > 0 && await FetchRawAsync(companyId, accession, doc, ct) is { } thinRaw)
        {
            var remaining = Math.Max(0, FilingSections.MaxScanChunks - chunks.Count);
            var perItem = Math.Max(MinChunksPerThinItem, remaining / thin.Count);
            foreach (var item in thin)
                chunks.AddRange(FilingSections.BuildSection(thinRaw, item, node, perItem));
        }

        // Every source shares one hard worker-call budget, including primary-filing Item 8 fallback.
        chunks = FilingSections.RankChunks(chunks, node, FilingSections.MaxScanChunks);

        // Record which parsed headings reached a worker so the UI can show document coverage.
        var keptHeadings = chunks
            .SelectMany(chunk => (chunk.Titles ?? []).Select(title => (chunk.Section, Title: title)))
            .ToHashSet();
        var report = headings
            .Select(h => new ScannedHeading(h.Section, h.Title, keptHeadings.Contains((h.Section, h.Title))))
            .ToList();

        // Publish the complete plan before RunFastWorkerAgentsAsync begins sending progress events.
        onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Planned, -1, 0,
            chunks.Select((c, i) => new FastWorkerChunkInfo(i, c.Item, c.Titles ?? [])).ToList()));

        var workerClaims = new List<ExtractionSuggestion>();
        var fastWorkerFindings = chunks.Count > 0
            ? await RunFastWorkerAgentsAsync(
                chunks, node, onProgress, strictCounterparties, workerClaims, ct)
            : [];
        var fastWorkerDigest = fastWorkerFindings.Count > 0
            ? BuildFastWorkerDigest(fastWorkerFindings, node)
            : "";
        var corpus = captureArtifacts
            ? chunks.Select((chunk, index) => new ExtractionChunkArtifact(
                index, chunk.Item, chunk.Titles ?? [], chunk.Text)).ToList()
            : null;
        return new FastWorkerScanResult(
            chunks.Count, fastWorkerFindings.Count, report, fastWorkerDigest, corpus, workerClaims);
    }

    // Calls FetchRawAsync and FilingSections.BuildHeadings; caches parsing shared by repeated runs.
    private async Task<List<FilingHeading>> GetOrParseHeadingsAsync(
        long companyId, string accession, string doc, ExtractionNode node, CancellationToken ct)
    {
        if (_cache.TryGetValue(HeadingsKey(accession, doc, node), out List<FilingHeading>? cached) && cached is not null)
            return cached;
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        var headings = raw is null
            ? []
            : FilingSections.BuildHeadings(raw, FilingSections.ItemsFor(node));
        _cache.Set(HeadingsKey(accession, doc, node), headings, CacheFor);
        return headings;
    }

    // Shared with the evidence viewer so it can reuse the filing downloaded by this service.
    public static string RawKey(string accession, string doc) => $"filing-raw:{accession}:{doc}";

    // Calls IStockApiClient for the primary EDGAR HTML and caches it for parsing and evidence display.
    private async Task<string?> FetchRawAsync(
        long companyId, string accession, string doc, CancellationToken ct)
    {
        if (_cache.TryGetValue(RawKey(accession, doc), out string? cached)) return cached;
        if (CompanyCik(companyId) is not { } cik) return null;

        var result = await _client.GetFilingDocument(cik, accession.Replace("-", ""), doc);
        if (string.IsNullOrWhiteSpace(result)) return null;
        _cache.Set(RawKey(accession, doc), result, CacheFor);
        return result;
    }

    // Calls ICompanyRepository and formats the CIK required by EDGAR archive requests.
    private string? CompanyCik(long companyId)
    {
        var company = _companies.GetById(companyId);
        return company is null || string.IsNullOrWhiteSpace(company.Cik) ? null : Cik.Trim(company.Cik);
    }

    // Combines nearby headings until the FilingSections size limit to reduce worker calls.
    private static List<FilingChunk> PackHeadingsIntoChunks(IReadOnlyList<FilingHeading> picked)
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

    // Calls RunFastWorkerAgentAsync in parallel, then merges duplicate source names.
    private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentsAsync(
        IReadOnlyList<FilingChunk> chunks, ExtractionNode node, Action<FastWorkerScanProgress>? onProgress,
        bool strictCounterparties, List<ExtractionSuggestion>? workerClaims, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallelFastWorkers);
        var perChunk = await Task.WhenAll(chunks.Select((c, i) =>
            RunFastWorkerAgentAsync(c, i, node, gate, onProgress, strictCounterparties, ct)));
        workerClaims?.AddRange(perChunk.SelectMany(claims => claims));

        var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in perChunk)
            foreach (var s in list)
            {
                if (string.IsNullOrWhiteSpace(s.Name)) continue;
                byName[s.Name] = byName.TryGetValue(s.Name, out var seen) ? MergeSuggestions(seen, s) : s;
            }
        return byName.Values.ToList();
    }

    // Merges two worker findings with the same name, keeping the evidence attached to the chosen value.
    private static ExtractionSuggestion MergeSuggestions(ExtractionSuggestion a, ExtractionSuggestion b)
    {
        var cls = a.Classification ?? b.Classification;
        var related = !string.IsNullOrWhiteSpace(a.RelatedCompany) ? a.RelatedCompany : b.RelatedCompany;
        var note = !string.IsNullOrWhiteSpace(a.Note) ? a.Note : b.Note;

        var evidence = !string.IsNullOrWhiteSpace(a.Evidence) ? a.Evidence : b.Evidence;

        return a with
        {
            Classification = cls,
            RelatedCompany = related,
            Note = note,
            Evidence = evidence,
        };
    }

    // Converts fast-worker findings into the digest used to build the lead-agent context.
    private static string BuildFastWorkerDigest(
        IReadOnlyList<ExtractionSuggestion> fastWorkerFindings, ExtractionNode node)
    {
        var label = node switch
        {
            ExtractionNode.COST => "cost counterparty candidates",
            ExtractionNode.RISK => "risk candidates",
            _                   => "revenue counterparty candidates",
        };
        var sb = new StringBuilder(
            $"PARALLEL-SCAN FINDINGS ({label} the worker agents pulled from the filing):\n");
        foreach (var s in fastWorkerFindings)
        {
            sb.Append("- ").Append(s.Name);
            if (node == ExtractionNode.RISK && s.Classification != null)
                sb.Append(" [").Append(s.Classification).Append(']');
            if (!string.IsNullOrWhiteSpace(s.RelatedCompany)) sb.Append(" | counterparty=").Append(s.RelatedCompany);
            if (!string.IsNullOrWhiteSpace(s.Note)) sb.Append(" | note=").Append(s.Note);
            sb.Append(" | from ").Append(s.Section).Append('\n');
            if (!string.IsNullOrWhiteSpace(s.Evidence))
                sb.Append("    evidence: \"").Append(s.Evidence).Append("\"\n");
        }
        return sb.ToString();
    }

    // Calls IChatLlm for one chunk, parses its JSON, and reports fast-worker progress.
    private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentAsync(
        FilingChunk chunk, int index, ExtractionNode node, SemaphoreSlim gate,
        Action<FastWorkerScanProgress>? onProgress, bool strictCounterparties, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Running, index, 0, null));
        var system = FastWorkerPromptFor(node, strictCounterparties);
        var prompt = $"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";
        // Retain the exact request for progress inspection and measurement errors.
        var transcript = $"━━ SYSTEM PROMPT ━━\n{system}\n\n━━ USER PROMPT ━━\n{prompt}";
        try
        {
            var completion = await _llm.CompleteAsync(
                new ChatRequest(system, prompt, FastWorkerMaxTokens, JsonObject: true, Fast: true), ct);
            var answer = completion.Content;
            var found = ParseFastWorkerResponse(answer, chunk.Section, node).ToList();

            // Retry once with a larger budget only when truncation produced no usable JSON.
            var retried = false;
            if (found.Count == 0 && !IsJsonObject(answer) &&
                string.Equals(completion.FinishReason, "length", StringComparison.OrdinalIgnoreCase))
            {
                retried = true;
                completion = await _llm.CompleteAsync(
                    new ChatRequest(system, prompt, FastWorkerRetryMaxTokens, JsonObject: true, Fast: true), ct);
                answer = completion.Content;
                found = ParseFastWorkerResponse(answer, chunk.Section, node).ToList();
            }

            // Report malformed output as an error rather than treating it as an empty finding set.
            if (!IsJsonObject(answer) && found.Count == 0)
            {
                var finish = completion.FinishReason is { Length: > 0 } reason
                    ? $"finish_reason={reason}"
                    : "finish_reason unavailable";
                var retry = retried
                    ? $" Retry with maxTokens={FastWorkerRetryMaxTokens} also failed."
                    : "";
                onProgress?.Invoke(new FastWorkerScanProgress(FastWorkerChunkPhase.Error, index, 0, null, transcript,
                    $"Reply was not valid JSON ({finish}).{retry} Raw reply:\n{answer}"));
                return found;
            }

            onProgress?.Invoke(new FastWorkerScanProgress(
                FastWorkerChunkPhase.Done, index, found.Count, null, transcript, answer));
            return found;
        }
        catch (Exception ex) when (
            !ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException)
        {
            onProgress?.Invoke(new FastWorkerScanProgress(
                FastWorkerChunkPhase.Error, index, 0, null, transcript, ex.Message));
            return [];
        }
        finally { gate.Release(); }
    }

    private static bool IsJsonObject(string answer)
    {
        using var probe = LlmJson.ParseObject(answer);
        return probe is not null;
    }

    // Calls LlmJson to parse the worker schema and salvage complete items from truncated output.
    private static IEnumerable<ExtractionSuggestion> ParseFastWorkerResponse(
        string answer, string section, ExtractionNode node)
    {
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null ||
            !doc.RootElement.TryGetProperty("sources", out var sources) ||
            sources.ValueKind != JsonValueKind.Array) yield break;

        foreach (var el in sources.EnumerateArray())
        {
            var name = ReadJsonText(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;
            yield return new ExtractionSuggestion(
                Name: name!,
                Classification: node == ExtractionNode.RISK ? ReadJsonText(el, "classification") : null,
                RelatedCompany: ReadJsonText(el, "related_company"),
                Section: section,
                Evidence: ReadJsonText(el, "evidence"),
                Note: ReadJsonText(el, "note"));
        }
    }

    // Reads a JSON field as text because providers may return evidence values as strings or numbers.
    private static string? ReadJsonText(JsonElement el, string prop)
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

#endregion

#region IFastWorkerScanService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/IFastWorkerScanService.cs
// NAMJENA: Definira javni ugovor servisa za skeniranje filing dokumenata.
// ============================================================================

using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

// Plans filing chunks, runs fast worker agents, and creates their digest.
public interface IFastWorkerScanService
{
    Task<IReadOnlyList<ExtractionSuggestion>> ScanFullSectionsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Runs the workers and creates a fresh digest for this extraction.
    Task<string> CreateFastWorkerDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        CancellationToken ct = default);

    // Builds deterministic filing chunks and runs the fast worker agents.
    Task<FastWorkerScanResult> RunFastWorkerScanAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        Action<FastWorkerScanProgress>? onProgress = null, bool strictCounterparties = false,
        bool captureArtifacts = false,
        CancellationToken ct = default);
}

#endregion

#region FilingAnalysisContextService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/FilingAnalysisContextService.cs
// NAMJENA: Pretvara svjezi digest brzih radnika u kontekst koji koristi vodeci model.
// ============================================================================

using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// Builds fast-worker findings shared by interactive chat and measurement consumers.
public sealed class FilingAnalysisContextService : IFilingAnalysisContextService
{
    private readonly IFastWorkerScanService _fastWorkerScan;

    public FilingAnalysisContextService(IFastWorkerScanService fastWorkerScan)
    {
        _fastWorkerScan = fastWorkerScan;
    }

    public async Task<string> BuildAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        bool scanIfMissing = true,
        string? fastWorkerDigest = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc)) return "";

        var digest = fastWorkerDigest is not null
            ? fastWorkerDigest
            : scanIfMissing
                ? await _fastWorkerScan.CreateFastWorkerDigestAsync(
                    companyId, accession, doc, node, ct)
                : "";
        return string.IsNullOrEmpty(digest) ? "" : "\n\n" + digest;
    }
}

#endregion

#region IFilingAnalysisContextService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/IFilingAnalysisContextService.cs
// NAMJENA: Definira ugovor za pripremu filing konteksta.
// ============================================================================

using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

public interface IFilingAnalysisContextService
{
    Task<string> BuildAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        bool scanIfMissing = true,
        string? fastWorkerDigest = null, CancellationToken ct = default);

}

#endregion

#region LeadAgentRunner
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/LeadAgentRunner.cs
// NAMJENA: Pokrece vodeci LLM u zavrsnom ili streaming nacinu te ponavlja prolaz kod prolaznih transportnih pogresaka.
// ============================================================================

using System.Runtime.CompilerServices;

namespace simple_bloomberg_terminal.Services.Extraction;

public sealed class LeadAgentRunner(
    IChatLlm llm,
    ILogger<LeadAgentRunner> logger) : ILeadAgentRunner
{
    private const int MaxCompletionAttempts = 2;

    // Measurement needs one atomic ledger, so use a bounded completion and retry the whole request
    // when the provider times out or closes its response body prematurely. Interactive chat continues
    // to use StreamAsync below because partial text is useful there.
    public async Task<LlmCompletion> CompleteAsync(
        string systemPrompt, string filingContext, string userPrompt, int maxTokens,
        CancellationToken ct = default)
    {
        var request = new ChatRequest(
            systemPrompt + filingContext,
            userPrompt,
            MaxTokens: maxTokens);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await llm.CompleteAsync(request, ct);
            }
            catch (Exception ex) when (
                attempt < MaxCompletionAttempts &&
                !ct.IsCancellationRequested &&
                ex is HttpRequestException or IOException or TaskCanceledException)
            {
                logger.LogWarning(
                    ex,
                    "Lead-agent completion transport failed on attempt {Attempt}/{MaxAttempts}; retrying",
                    attempt,
                    MaxCompletionAttempts);
            }
        }
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string systemPrompt, string filingContext, IReadOnlyList<LlmMessage> messages,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var request = new List<LlmMessage>
        {
            new("system", systemPrompt + filingContext)
        };
        request.AddRange(messages);

        await foreach (var delta in llm.StreamAsync(request, ct: ct))
            yield return delta;
    }
}

#endregion

#region ILeadAgentRunner
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/ILeadAgentRunner.cs
// NAMJENA: Definira ugovor vodeceg LLM agenta.
// ============================================================================

namespace simple_bloomberg_terminal.Services.Extraction;

// Executes a lead-agent request without imposing chat, save-block, or measurement semantics.
public interface ILeadAgentRunner
{
    Task<LlmCompletion> CompleteAsync(
        string systemPrompt, string filingContext, string userPrompt, int maxTokens,
        CancellationToken ct = default);

    IAsyncEnumerable<ChatDelta> StreamAsync(
        string systemPrompt, string filingContext, IReadOnlyList<LlmMessage> messages,
        CancellationToken ct = default);
}

#endregion

#region CounterpartyPrompts
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/CounterpartyPrompts.cs
// NAMJENA: Sadrzi promptove za COST i REVENUE protustranke, ukljucujuci blazi i strozi nacin ekstrakcije.
// ============================================================================

using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

// Shared filing-worker contract for the two relationship nodes. The node supplies direction, so the
// model only has to identify a named company and prove the commercial relationship.
public static class CounterpartyPrompts
{
    public const string Version = "directional-counterparty-worker-v2";

    public static string FastWorkerSystemPrompt(ExtractionNode node, bool strict = false)
    {
        var direction = node switch
        {
            ExtractionNode.COST =>
                "Find only COST-SIDE counterparties: named suppliers, vendors, manufacturers, foundries, " +
                "contract producers, licensors, or service providers from which the filer buys goods, " +
                "rights, or services.",
            ExtractionNode.REVENUE =>
                "Find only REVENUE-SIDE counterparties: named customers, buyers, licensees, distributors, " +
                "resellers, or commercial partners through which the filer earns or expects to earn revenue. " +
                "The relationship must be revenue-generating for the filer.",
            _ => throw new ArgumentOutOfRangeException(nameof(node), node, "Counterparty prompts apply only to COST and REVENUE.")
        };

        return
            "You extract NAMED COUNTERPARTIES from one plain-text excerpt of a US public company's SEC filing. " +
            direction + " A counterparty must be a named company and the excerpt must establish an explicit " +
            "commercial relationship. Do not use outside knowledge. Do not return business segments, products, " +
            "regions, industries, unnamed customer or supplier concentrations, competitors, litigation adversaries, " +
            "acquisition targets, or companies merely mentioned without the required relationship. Do not derive " +
            "values or percentages from financial tables or company-wide figures. A relationship with no stated " +
            "amount is valid. For every result, write evidence first as one verbatim substring that names the " +
            "counterparty and establishes the relationship. Then return name exactly as written, related_company " +
            "as the same name and a short note describing the relationship. Reply " +
            $"with JSON only: {{\"sources\":[{{\"evidence\":\"\",\"name\":\"\",\"related_company\":\"\"," +
            $"\"note\":\"\"}}]}}. If the excerpt establishes no matching " +
            "counterparty, reply {\"sources\":[]}." +
            (strict
                ? " STRICT MODE: require the excerpt itself to state the purchase, sale, supply, license, " +
                  "distribution, resale, or concrete collaboration. A product mention, compatibility statement, " +
                  "industry list, or description of a company as a market leader is insufficient. If uncertain, omit it."
                : "");
    }
}

#endregion

#region RiskPrompts
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/RiskPrompts.cs
// NAMJENA: Sadrzi prompt brzog radnika za ekstrakciju rizika.
// ============================================================================

namespace simple_bloomberg_terminal.Services.Extraction;

public static class RiskPrompts
{
    public const string Version = "risk-worker-v2";

    public const string FastWorkerSystemPrompt =
        "You extract RISKS disclosed by one US public company from one plain-text SEC filing excerpt. " +
        "Use only this excerpt and return only clearly evidenced risks; do not guess or use outside knowledge. " +
        "For each risk, write evidence first as one verbatim substring, then provide a short name, classification " +
        "as exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL, and a " +
        "one- or two-sentence note. Reply with JSON only: {\"sources\":[{\"evidence\":\"\",\"name\":\"\"," +
        "\"classification\":\"BUSINESS\",\"note\":null}]}. If the excerpt establishes no risk, reply " +
        "{\"sources\":[]}.";
}

#endregion

#region ExtractionChatService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/Chat/ExtractionChatService.cs
// NAMJENA: Povezuje filing kontekst, povijest razgovora i vodeci model te definira save format za COST, REVENUE i RISK.
// ============================================================================

using System.Runtime.CompilerServices;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

// Conversational adapter over the shared filing-context and lead-agent services.
public sealed class ExtractionChatService : IExtractionChatService
{
    private readonly IFilingAnalysisContextService _context;
    private readonly ILeadAgentRunner _leadAgent;

    public ExtractionChatService(
        IFilingAnalysisContextService context,
        ILeadAgentRunner leadAgent)
    {
        _context = context;
        _leadAgent = leadAgent;
    }

    private static string LeadAgentPromptFor(ExtractionNode node) => node switch
    {
        ExtractionNode.COST =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the COUNTERPARTY candidates below, each with the VERBATIM proof text " +
            "they found. Ground every claim in those findings; if something isn't there, say so " +
            "rather than guessing - never name a company " +
            "that does not appear in the findings. Help the user review and decide which counterparty " +
            "relationships to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific counterparty, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\"," +
            "\"related_company\":null,\"related_company_ticker\":null,\"reference\":null," +
            "\"evidence\":\"\"}\n```\n" +
            "name is the supplier company's name. " +
            "Return only one record per counterparty. If the same counterparty appears more than once or " +
            "under minor variations of the same name, merge those findings and keep the clearest verbatim evidence. " +
            "related_company is the same counterparty name; when " +
            "it's a publicly traded company you can identify, also set related_company_ticker to its " +
            "stock ticker (else null) so it can be enriched. reference is the verbatim passage (name " +
            "the SEC Item or note, then the source text) this record was drawn from. evidence is ONE " +
            "VERBATIM excerpt substring backing this record - quote enough to identify any figure you " +
            "report. Emit one save block per counterparty the user confirms, alongside your normal reply.",

        ExtractionNode.RISK =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the RISK candidates below, each with the VERBATIM proof text they " +
            "found. Ground every claim in those findings; if something isn't there, say so rather " +
            "than guessing. Help the user review and " +
            "decide which disclosed risks to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific risk, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"BUSINESS\",\"note\":null,\"reference\":null," +
            "\"evidence\":\"\"}\n```\n" +
            "classification is the risk scope, exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, " +
            "LEGAL_REGULATORY, FINANCIAL, GENERAL. note is one or two sentences summarising the risk; " +
            "use null when not stated. reference is the verbatim passage (name the SEC Item - 1A risk " +
            "factors / 7A market risk - then the source text) this whole risk record was drawn from. " +
            "evidence is ONE VERBATIM excerpt substring backing this record. Emit one save block per " +
            "risk the user confirms, alongside your normal reply.",

        _ =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported named REVENUE COUNTERPARTIES with verbatim proof. Use only those " +
            "findings. Never invent a company, infer a " +
            "relationship from outside knowledge, or turn a segment, product, region, industry, or unnamed " +
            "customer concentration into a source. Review only named customers, buyers, licensees, " +
            "distributors, resellers, and commercial revenue partners. Aggregate financial figures do " +
            "not establish a value for a specific counterparty, so never attach them to one. " +
            "A counterparty relationship with no stated amount is valid. Be concise.\n\n" +
            "When the user wants to SAVE a specific counterparty, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"related_company\":\"\",\"related_company_ticker\":null," +
            "\"reference\":null,\"evidence\":\"\"}\n```\n" +
            "name and related_company are the customer company's name. Return only one record per counterparty. " +
            "If the same counterparty " +
            "appears more than once or under minor variations of the same name, merge those findings and " +
            "keep the clearest verbatim evidence. Set related_company_ticker only when the filing context identifies " +
            "it reliably. reference names the SEC Item or note and includes the source passage. evidence is " +
            "one verbatim excerpt substring that names the company and establishes the commercial relationship. " +
            "Emit one save block per counterparty the user confirms, alongside your normal reply.",
    };

    public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history,
        string? fastWorkerDigest = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
        if (hasFiling && fastWorkerDigest is null)
            yield return new ChatDelta("status", "Scanning the filing with parallel fast worker agents...");

        var filingContext = await _context.BuildAsync(
            companyId, accession, doc, node,
            scanIfMissing: fastWorkerDigest is null,
            fastWorkerDigest: fastWorkerDigest,
            ct: ct);
        var messages = history
            .Select(message => new LlmMessage(
                message.Role == "assistant" ? "assistant" : "user", message.Content))
            .ToList();
        await foreach (var delta in _leadAgent.StreamAsync(
            LeadAgentPromptFor(node), filingContext, messages, ct))
            yield return delta;
    }
}

#endregion

#region IExtractionChatService
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/Chat/IExtractionChatService.cs
// NAMJENA: Definira ugovor razgovornog dijela filing-ekstrakcije.
// ============================================================================

using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

// Provides live chat over one SEC filing and emits structured save blocks for form prefilling.
// The client resends visible turns while private filing context is rebuilt on the server.
public interface IExtractionChatService
{
    IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history,
        string? fastWorkerDigest = null,
        CancellationToken ct = default);
}

#endregion

#region ScanJobStore
// ============================================================================
// DATOTEKA: simple-bloomberg-terminal/Services/Extraction/Chat/ScanJobStore.cs
// NAMJENA: Cuva stanje pozadinskog skeniranja, napredak pojedinih radnika, rezultat i razgovorne meduspremnike.
// ============================================================================

using System.Collections.Concurrent;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction.Chat;

public enum ScanJobStatus { Running, Done, Error }

// Live state for one parallel agent call, including bundled titles and its current status.
public class ScanChunkState
{
    public IReadOnlyList<string> Titles { get; init; } = [];
    public string Status { get; set; } = "Queued";
    public int Found { get; set; }
    // Stores the exact prompt and response so the widget can inspect each call and its failures.
    public string Prompt { get; set; } = "";
    public string Response { get; set; } = "";
}

// Groups the agent calls for one SEC Item so the widget can display them together.
public class ScanSection
{
    public string Item { get; init; } = "";
    public List<ScanChunkState> Chunks { get; } = new();
}

// State for one detached scan. It lives in the singleton store so work survives the starting request
// and the widget can poll it after navigation.
public class ScanJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public long CompanyId { get; init; }
    public string CompanyName { get; init; } = "";
    public string Accession { get; init; } = "";
    public string Doc { get; init; } = "";
    public string Node { get; init; } = "REVENUE";
    public bool StrictCounterparties { get; init; }
    public string? Form { get; init; }
    public string FilingLabel { get; init; } = "";   // e.g. "10-K 2024-01-31" for the widget header

    public ScanJobStatus Status { get; set; } = ScanJobStatus.Running;
    public string Progress { get; set; } = "Queued…"; // live phase text shown while running

    // The live scan tree is shared by concurrent workers and status polling, so access requires the lock.
    public List<ScanSection> Sections { get; } = new();
    public List<ScanChunkState> ChunkList { get; } = new();  // flat, index-aligned with the scan plan
    public object SectionsLock { get; } = new();
    public FastWorkerScanResult? Report { get; set; }
    public string Summary { get; set; } = "";        // auto AI prose shown first in the widget
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    // Detached reply buffers let chat generation continue across navigation while the widget polls updates.
    public bool Replying { get; set; }
    public string ReplyBuffer { get; set; } = "";    // incremental answer text
    public string ReplyThink { get; set; } = "";     // incremental reasoning/thinking
    public string? ReplyError { get; set; }
}

// Tracks detached scans across requests. The browser keeps job IDs locally; this single-user store
// intentionally has no per-user partitioning.
public class ScanJobStore
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();

    public void Add(ScanJob job) => _jobs[job.Id] = job;

    public ScanJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}

#endregion

#endif

