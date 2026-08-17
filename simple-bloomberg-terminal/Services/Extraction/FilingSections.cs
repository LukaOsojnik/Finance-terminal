using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Extraction;

/// <summary>One slice of a filing handed to a single worker call. <paramref name="Section"/> is the
/// provenance label; <paramref name="Item"/> is the SEC Item it belongs to (for grouping in the widget)
/// and <paramref name="Titles"/> are the sub-heading titles this chunk bundles (one call may pack
/// several small headings — see the heading-packing in FilingExtractionService).</summary>
public record FilingChunk(string Section, string Text, string Item = "", IReadOnlyList<string>? Titles = null);

/// <summary>A bold sub-heading within a target Item, plus the text under it (until the next heading).</summary>
public record FilingHeading(string Section, string Title, string Body);

/// <summary>
/// Turns a raw SEC filing (HTML or plain text) into small, targeted chunks for the extractor:
/// strip markup → keep only the revenue-relevant Items (1A risk factors, 7 MD&amp;A, 8 financial
/// notes) → split each into paragraph chunks so the LLM calls stay cheap. Pure functions, no I/O.
///
/// Tables are the exception to "strip markup": they are re-rendered by <see cref="FilingTables"/> and
/// travel through the pipeline as HTML, because a financial table flattened to text is just a run of
/// digits with no columns. They are never split across chunks and never separated from their caption.
/// </summary>
public static class FilingSections
{
    // Which SEC Items each extraction node reads, for a given SEC form. Revenue/cost breakdowns live
    // in Item 7 (MD&A) and Item 8 (notes); risks live in Item 1A (risk factors) + Item 7A (market
    // risk). Ordered by priority so the first item isn't starved of chunk slots downstream.
    //
    // <paramref name="form"/> is the EDGAR form type ("10-K", "8-K/A", …). Only REVENUE varies by
    // form today: an 8-K is numbered on a completely different scheme (Item 2.02, not Item 7), so
    // the annual Items find nothing in one. COST and RISK keep their annual Items regardless — that
    // is unchanged behaviour, and on an 8-K they simply come up empty as they do today.
    public static string[] ItemsFor(ExtractionNode node, string? form = null) => node switch
    {
        ExtractionNode.RISK => ["1A", "7A"],
        // COST adds Item 1 (Business) — "Sources & Availability of Raw Materials" is the canonical
        // named-supplier / single-source disclosure, which the financial Items don't carry.
        ExtractionNode.COST => ["1", "7", "8"],
        // REVENUE on an 8-K (current report):
        //   1.01 entry into a material definitive agreement — customer wins, named contracts
        //   2.02 results of operations — the quarterly revenue detail
        //   8.01 other events — where contract announcements land when they miss 1.01
        _ when IsCurrentReport(form) => ["1.01", "2.02", "8.01"],
        // REVENUE on a 10-K:
        //   1  Business — customers, distribution channels, backlog, JV partners
        //   1A Risk Factors — concentration framed as risk ("loss of Customer A"), which names
        //      counterparties the ASC 275-10-50 note states only as a percentage
        //   7  MD&A — revenue drivers, named large contracts, constant-currency detail. Its segment
        //      TABLES duplicate ASC 280, but the driver narrative appears nowhere else in the filing
        //   8  the notes, which are the core of it: segment + geographic revenue (ASC 280), the
        //      disaggregation table and remaining performance obligations (ASC 606), customer
        //      concentration (ASC 275-10-50), equity-method investments and JV partners (ASC 323)
        // Item 5 is deliberately absent: it is repurchase/dividend context, not revenue.
        _ => ["1", "1A", "7", "8"],
    };

    /// <summary>An 8-K (or 8-K/A) — the current report, numbered Item 1.01 / 2.02 / 8.01 rather than
    /// on the annual 10-K scheme.</summary>
    private static bool IsCurrentReport(string? form) =>
        form is not null && form.Trim().StartsWith("8-K", StringComparison.OrdinalIgnoreCase);

    public const int MaxChunkChars = 4000;         // ~1k tokens/chunk — the per-worker text budget
    private const int MaxChunksPerSection = 12;    // keep one giant section from hogging every slot
    /// <summary>
    /// The whole-scan ceiling on worker calls for ONE node on ONE filing, sized at
    /// <c>MaxChunksPerSection</c> × the widest routing (REVENUE's four Items: 1, 1A, 7, 8).
    ///
    /// It must stay in step with <see cref="ItemsFor"/>: inside <see cref="Build"/> the cap is
    /// checked AFTER the per-section break, so at 3×12 a fourth Item got exactly one chunk before
    /// Build returned — and Item 8, the one carrying the actual figures, is last in document order
    /// and so was the Item that lost.
    ///
    /// It is public because <c>Build</c> is no longer the only path that needs it: the auto-scan
    /// assembles its chunks from three independent feeds (heading triage, Item 8 reports, and a
    /// sequential read of any Item whose outline was too thin) and needs the same single ceiling
    /// across all three. Without it the thin-Item feed alone reached 120 calls — 3 Items × the 40
    /// <see cref="BuildSection"/> defaults to — on exactly the filings where targeting is worst.
    /// </summary>
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

    /// <summary>
    /// Sequential chunks of one Item's full body — used for Item 8, whose financial tables are detached
    /// from their bold headings (so heading-based chunking mislabels and truncates them), and for any
    /// narrative Item whose heading outline was too thin to triage. Reuses the same Item-detection +
    /// paragraph packing as <see cref="Build"/>. Tables stay whole (a single table is one paragraph)
    /// unless one exceeds <see cref="MaxChunkChars"/>, which is then clipped.
    ///
    /// When the body yields more than <paramref name="maxChunks"/>, the survivors are chosen by
    /// <see cref="RankChunks"/> rather than by position. Document-order truncation was the wrong rule
    /// here for the same reason it was wrong in <see cref="Build"/>: the disaggregation and segment
    /// tables sit LATE in both Item 7 and Item 8, so "first N" reliably kept boilerplate and dropped
    /// every figure. The kept set is still handed back in document order.
    /// </summary>
    public static List<FilingChunk> BuildSection(string raw, string item, ExtractionNode node, int maxChunks = 40)
    {
        var body = SectionBody(ToText(raw), item);
        if (body is null) return [];
        var chunks = Paragraphs(body)
            .Select(text => new FilingChunk($"Item {item}", text, $"Item {item}"))
            .ToList();
        return RankChunks(chunks, node, maxChunks);
    }

    // A rendered table outweighs any amount of prose keyword-dropping: the workers are extracting
    // figures, and in a filing the figures are in tables. Set above the realistic keyword count for a
    // single paragraph so a table chunk never loses its slot to prose that merely mentions "segment".
    private const int TableScore = 5;

    /// <summary>
    /// Keep the <paramref name="take"/> chunks most likely to carry <paramref name="node"/>'s data,
    /// returned in document order. Scored on the same <see cref="Keywords"/> vocabulary
    /// <see cref="SelectReport"/> already uses on report titles, plus a bonus for carrying a rendered
    /// table.
    ///
    /// Deliberately NOT an LLM triage call like <c>TriageHeadingsAsync</c>. That one reasons over ~120
    /// headings spanning four Items, which is a wide enough decision to be worth a model; this ranks at
    /// most a few dozen paragraphs of ONE section, and spending a call to decide how to spend calls is
    /// a poor trade at that width. Scoring is also deterministic, so the chunk plan a filing produces
    /// is testable without a provider.
    /// </summary>
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
        var score = text.Contains("<td", StringComparison.Ordinal) ? TableScore : 0;
        foreach (var keyword in Keywords(node))
            if (text.Contains(keyword, StringComparison.OrdinalIgnoreCase)) score++;
        return score;
    }

    // ── SEC-rendered reports (the R*.htm financial statements) ────────────────────────────────────

    /// <summary>
    /// Which of a filing's indexed reports (see <see cref="FilingReportReader"/>) are worth fetching
    /// for this node. Runs over the <c>FilingSummary.xml</c> index BEFORE anything is downloaded, so it
    /// is the one thing standing between a scan and ~60 SEC round-trips per filing.
    /// </summary>
    /// <remarks>
    /// A typical 10-K index looks like:
    /// <code>
    ///   Cover      | Cover Page
    ///   Statements | CONSOLIDATED STATEMENTS OF OPERATIONS
    ///   Statements | CONSOLIDATED BALANCE SHEETS
    ///   Statements | CONSOLIDATED BALANCE SHEETS (Parenthetical)
    ///   Statements | CONSOLIDATED STATEMENTS OF CASH FLOWS
    ///   Notes      | Revenue
    ///   Notes      | Segment Information and Geographic Data
    ///   Tables     | Revenue (Tables)
    ///   Details    | Revenue - Net Sales Disaggregated by Significant Products (Details)
    /// </code>
    /// </remarks>
    public static bool SelectReport(ReportEntry entry, ExtractionNode node)
    {
        // RISK reads Items 1A/7A, which are narrative; every rendered report is a financial
        // statement, so none of them apply. (ScanAutoAsync only asks for reports when the node's
        // items include 8, so RISK never gets here — this is belt-and-braces.)
        if (node == ExtractionNode.RISK) return false;

        // "(Parenthetical)" holds only share counts and par values, and "Additional Information" is
        // each note's catch-all of loose scalar facts. Both are noise for every node, and between
        // them they account for a third of a filing's reports.
        if (entry.ShortName.Contains("Parenthetical", StringComparison.OrdinalIgnoreCase) ||
            entry.ShortName.Contains("Additional Information", StringComparison.OrdinalIgnoreCase))
            return false;

        return entry.Category switch
        {
            // Of the six or so statements, only the income statement carries revenue and cost lines.
            // Measured on Apple FY2023 and AMD FY2025: the balance sheet, cash flow, equity and
            // comprehensive-income reports are 50-56% of the entire Item 8 payload (4 of 9-11 worker
            // calls per node) and name no revenue or cost line at all — Apple's balance sheet opens on
            // cash, investments and receivables. The "anchor totals" they used to be taken for are
            // checked in code by ExtractionChatService.SumCheck over tagged XBRL facts, not by the
            // model reading them. The income statement stays: it is the only place the pipeline sees
            // R&D, restructuring, acquisition amortization and the product-vs-service cost-of-sales
            // split, none of which XbrlFacts.Cogs/Opex can surface (AMD even tags its main cost line
            // as CostOfGoodsAndServiceExcludingDepreciationDepletionAndAmortization, absent there).
            "Statements" => IsIncomeStatement(entry.ShortName),
            // The disaggregated tables: revenue by product/segment/geography, the segment operating
            // income reconciliation, supply concentrations. This is the good stuff, but there are
            // ~39 per filing, so take only the ones whose topic matches the node.
            "Details" => Keywords(node).Any(k => entry.ShortName.Contains(k, StringComparison.OrdinalIgnoreCase)),
            // Cover (entity metadata), Notes (the same content as prose, which already reaches the
            // model through the Item 7 heading path), Tables (empty layout templates) and Policies
            // (accounting-policy prose) carry nothing the workers need.
            _ => false,
        };
    }

    /// <summary>
    /// Is this <c>Statements</c> report the income statement? Filers title it "… of Operations",
    /// "… of Income" or "… of Earnings". "Comprehensive Income" is stripped first because that is a
    /// DIFFERENT statement which also contains the word "Income", and because some filers combine the
    /// two under one title ("Statements of Operations and Comprehensive Income") — which must be kept.
    /// </summary>
    /// <remarks>
    /// Deliberately an include rule rather than a list of statements to exclude. An exclude rule is more
    /// tolerant of unseen titles, but it has to enumerate every non-P&amp;L statement to stay correct and
    /// it gets the combined title above backwards. The accepted risk is the mirror image: a filer whose
    /// income statement uses none of the three words loses it. Dropping the income statement is the worse
    /// failure, so if that ever shows up in practice, widen the word list rather than inverting the rule.
    /// </remarks>
    private static bool IsIncomeStatement(string shortName)
    {
        var s = shortName.Replace("Comprehensive Income", "", StringComparison.OrdinalIgnoreCase);
        return s.Contains("Operations", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Income", StringComparison.OrdinalIgnoreCase)
            || s.Contains("Earnings", StringComparison.OrdinalIgnoreCase);
    }

    // Topic words that mark a chunk or a Details report as relevant to a node — matched against a
    // report's ShortName by SelectReport, and against a chunk's text by RankChunks. Drawn from the
    // section titles filers actually use ("Segment Information and Geographic Data - Net Sales
    // (Details)"), which are conventional enough across filers to match on.
    private static string[] Keywords(ExtractionNode node) => node switch
    {
        // RISK never reaches SelectReport (it returns false for the node before this is called), so
        // these exist for RankChunks alone. The obvious word — "risk" — is deliberately absent: every
        // paragraph of Item 1A is about risk, so it scores the whole section equally and ranks
        // nothing. What discriminates inside a risk section is the SPECIFIC exposure named, which is
        // also what the node is trying to extract.
        ExtractionNode.RISK =>
            ["Concentration", "Depend", "Single Source", "Sole Source", "Litigation", "Regulat",
             "Interest Rate", "Foreign Currency", "Exchange Rate", "Cyber", "Supply", "Tariff"],
        ExtractionNode.COST =>
            ["Cost", "Expense", "Operating Income", "Segment", "Geographic", "Supply", "Purchase Obligation"],
        // REVENUE, one word per note the Item 8 routing targets: segment/geographic (ASC 280),
        // disaggregation + remaining performance obligations (ASC 606), major-customer concentration
        // (ASC 275-10-50), equity-method investments and JV partners (ASC 323). "Contract" is left
        // out on purpose — it would also drag in the contractual-obligation maturity tables, which
        // are liabilities, not revenue; "Disaggregat" and "Performance Obligation" cover ASC 606.
        _ =>
            ["Revenue", "Net Sales", "Segment", "Geographic", "Product", "Customer", "Disaggregat",
             "Performance Obligation", "Concentration", "Equity Method"],
    };

    /// <summary>
    /// Chunks from the SEC's rendered reports — the Item 8 path. Each report is already one clean
    /// statement table, so there is nothing to detect or segment: render it, tag it with the report's
    /// own title (which carries the units, e.g. "CONSOLIDATED BALANCE SHEETS - USD ($) $ in Millions"),
    /// and pack consecutive tables up to the same per-worker budget the rest of the pipeline uses.
    /// </summary>
    public static List<FilingChunk> BuildReports(IReadOnlyList<FilingReport> reports)
    {
        var chunks = new List<FilingChunk>();
        var sb = new StringBuilder();
        var titles = new List<string>();

        void Flush()
        {
            if (sb.Length == 0) return;
            chunks.Add(new FilingChunk("Item 8", sb.ToString(), "Item 8", titles));
            sb = new StringBuilder();
            titles = new List<string>();
        }

        foreach (var report in reports)
            foreach (var table in FilingTables.RenderAll(report.Html, report.ShortName))
            {
                // A single statement table can exceed the budget on its own; it still goes whole,
                // because dropping rows off a balance sheet is worse than one oversized call.
                if (sb.Length > 0 && sb.Length + table.Length > MaxChunkChars) Flush();
                if (sb.Length > 0) sb.Append("\n\n");
                sb.Append(table);
                if (!titles.Contains(report.ShortName)) titles.Add(report.ShortName);
            }
        Flush();
        return chunks;
    }

    /// <summary>Does this look like an HTML document rather than a plain-text filing?</summary>
    private static bool LooksHtml(string raw) =>
        Regex.IsMatch(raw[..Math.Min(raw.Length, 2000)], "<html|<body|<div|<p|<table", RegexOptions.IgnoreCase);

    // Stands in for a rendered table while the surrounding markup is flattened to text — a control
    // character, so nothing in a filing can collide with it.
    private const char TableMark = '';

    // Strip HTML to readable text, preserving paragraph/row boundaries as newlines (InnerText alone
    // merges words across tags) and preserving TABLES AS TABLES. InnerText over a <table> puts no
    // delimiter between <td>s, so a row reads "Americas$162,560(4)%" — the columns are simply gone.
    // Each data table is therefore lifted out and re-rendered by FilingTables before the flattening,
    // then spliced back in as its own paragraph. Plain-text filings skip all of that (no markup to
    // find tables in) and pass through the same whitespace normalisation.
    private static string ToText(string raw)
    {
        var tables = new List<string>();
        if (LooksHtml(raw))
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(raw);
            doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

            // Outermost tables only — Render walks a table's own rows, so descending into a nested one
            // would emit it twice. A layout table (Render returns null) is left in place so the prose
            // SEC filings wrap in tables still reaches the text.
            foreach (var t in doc.DocumentNode.SelectNodes("//table")?.ToList() ?? [])
            {
                if (t.Ancestors("table").Any()) continue;
                if (FilingTables.Render(t) is not { } rendered) continue;
                tables.Add(rendered);
                t.ParentNode.ReplaceChild(
                    doc.CreateTextNode($"\n\n{TableMark}{tables.Count - 1}{TableMark}\n\n"), t);
            }

            // Force a line break where blocks close, so text nodes don't run together.
            var marked = Regex.Replace(doc.DocumentNode.OuterHtml, "(?i)</(p|div|tr|li|h[1-6]|table)>", "\n");
            marked = Regex.Replace(marked, "(?i)<br\\s*/?>", "\n");
            var flat = new HtmlDocument();
            flat.LoadHtml(marked);
            raw = HtmlEntity.DeEntitize(flat.DocumentNode.InnerText) ?? "";
        }

        // Normalise: tidy each line, collapse blank runs to a single paragraph break.
        var lines = raw.Replace("\r", "").Split('\n')
            .Select(l => Regex.Replace(l, "[ \t ]+", " ").Trim());
        var sb = new StringBuilder();
        int blanks = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) { blanks++; if (blanks <= 1) sb.Append('\n'); continue; }
            blanks = 0;
            sb.Append(line).Append('\n');
        }

        // Put the rendered tables back where their markers sit.
        return tables.Count == 0
            ? sb.ToString()
            : Regex.Replace(sb.ToString(), $"{TableMark}(\\d+){TableMark}",
                m => tables[int.Parse(m.Groups[1].Value)]);
    }

    /// <summary>
    /// The canonical Reg S-K title for each Item the extractor targets. Not every filer prints the
    /// Item number in the body: Intel's 10-K contains the string "Item 1A." exactly once, in the
    /// contents table, and heads the actual section "Risk Factors". Number-only detection finds the
    /// contents line and nothing else, which collapsed a 3.3 MB filing to two chunks. These titles are
    /// prescribed by regulation, so matching them is as canonical as matching the number.
    ///
    /// Item 1 ("Business") is deliberately absent — one generic word, far too easy to false-match.
    /// </summary>
    private static readonly (string Num, string Title)[] ItemTitles =
    [
        ("1A", @"Risk\s+Factors"),
        ("7A", @"Quantitative\s+and\s+Qualitative\s+Disclosures?\s+About\s+Market\s+Risk"),
        ("7",  @"Management'?.?s\s+Discussion\s+and\s+Analysis(\s+of\s+Financial\s+Condition.*)?"),
        ("8",  @"Financial\s+Statements\s+and\s+Supplementary\s+Data"),
    ];

    /// <summary>
    /// An Item heading at the start of a line, capturing the number. The decimal part is what makes
    /// 8-Ks readable: they number on the Item 2.02 scheme, and without <c>(?:\.\d+)?</c> "Item 2.02"
    /// captures "2" — every 8-K Item would collapse onto its whole-number prefix. 10-K numbering is
    /// unaffected, since the group only takes a dot that is FOLLOWED by digits ("Item 7." still
    /// yields "7").
    /// </summary>
    private const string ItemHeadingPattern = @"^[#>*_\s]*Item\s+(\d+(?:\.\d+)?[A-Z]?)\b";

    /// <summary>The Item this line heads, by number ("Item 1A.", "Item 2.02") or by canonical title
    /// ("Risk Factors"), or null. Leading markdown markers (#, *, _, &gt;) are tolerated. The title
    /// form must be the WHOLE line, so the countless mid-sentence references ('see "Risk Factors"
    /// above') are not mistaken for section boundaries.</summary>
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

    // The real body of "Item N": of every place that heading appears (TOC + body), take the one with
    // the most text before the next DIFFERENT Item — that is the section, not the contents-page line.
    //
    // "Different" matters: filers repeat the section title as a running page header, so Intel's risk
    // factors carry ~15 "Risk Factors" lines through the section. Ending at the next heading of ANY
    // kind would cut the section at its own first page break.
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

    private const int CaptionMaxChars = 500;   // a table's lead-in sentence, not a whole paragraph

    // Pack paragraphs (blank-line separated) into <= MaxChunkChars chunks without splitting a
    // paragraph. A single oversized paragraph is clipped — UNLESS it's a table, which is emitted whole
    // (clipping a financial statement mid-table would drop rows of figures).
    //
    // A table also drags its caption along. The sentence that introduces a filing's table is a separate
    // paragraph ("The following table shows net sales by reportable segment ... (dollars in millions):")
    // and it is where the units and the subject live — a table that lands at a chunk boundary without it
    // is a grid of bare numbers with no scale.
    private static IEnumerable<string> Paragraphs(string body)
    {
        var paras = Regex.Split(body, "\n\\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0);
        var current = new StringBuilder();
        string? prev = null;   // the paragraph before this one — a table's caption candidate
        foreach (var para in paras)
        {
            var isTable = FilingTables.IsTableBlock(para);
            var caption = isTable && prev is { Length: > 0 and <= CaptionMaxChars } ? prev : null;

            if (para.Length > MaxChunkChars)
            {
                if (current.Length > 0) { yield return current.ToString(); current.Clear(); }
                yield return isTable
                    ? (caption is null ? para : caption + "\n\n" + para)
                    : para[..MaxChunkChars];
                prev = para;
                continue;
            }

            if (current.Length > 0 && current.Length + para.Length > MaxChunkChars)
            {
                yield return current.ToString();
                current.Clear();
                if (caption is not null) current.Append(caption);   // carried into the new chunk
            }
            if (current.Length > 0) current.Append("\n\n");
            current.Append(para);
            prev = para;
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

    /// <summary>
    /// Bold sub-headings within the target Items, each paired with the text under it (until the next
    /// heading). A heading = a whole line rendered bold (the filing puts each on its own row), of any
    /// length. The user picks from these; one worker then reads one heading's body. Plain-text
    /// filings (no markup to detect bold) return an empty list — the caller falls back to auto-scan.
    /// </summary>
    public static List<FilingHeading> BuildHeadings(string raw, string[] items)
    {
        // Old filings are submitted as plain text with no markup to detect bold in — parse those
        // line-by-line. Everything since ~2001 is HTML and goes through the DOM bold-detection below.
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

    // Flat-text sibling of BuildHeadings, for plain-text filings. A sub-heading is a markdown ATX
    // heading line ("## Title") or a wholly-bold line ("**Title**"); body = the lines under it until
    // the next heading. Same Item-scoping and dedupe as the HTML path, minus the DOM walk.
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
                    // Seed the Item's own title so the lead-in before the first sub-heading is
                    // captured (e.g. Item 8's financial-statement tables, which otherwise fall in
                    // the gap between the Item line and the first bold sub-heading and get dropped).
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

    // Depth-first walk that emits a line at every block boundary and <br>, tracking per-line boldness.
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
            else if (string.Equals(child.Name, "table", StringComparison.OrdinalIgnoreCase))
            {
                // Emit a data table as ONE rendered line rather than walking its cells — the walk
                // would run the figures together exactly as ToText used to. It is never a heading, so
                // it goes in non-bold. A layout table falls through to the normal walk so its prose
                // (and any bold heading inside it) is still seen.
                FlushLine(lines, acc);
                if (FilingTables.Render(child) is { } rendered) lines.Add((rendered, false));
                else CollectLines(child, lines, acc);
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
