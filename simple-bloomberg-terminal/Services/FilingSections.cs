using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services;

/// <summary>One paragraph-sized slice of a filing, tagged with the SEC Item it came from.</summary>
public record FilingChunk(string Section, string Text);

/// <summary>A bold sub-heading within a target Item, plus the text under it (until the next heading).</summary>
public record FilingHeading(string Section, string Title, string Body);

/// <summary>
/// Turns a raw SEC filing (HTML or plain text) into small, targeted text chunks for the extractor:
/// strip markup → keep only the revenue-relevant Items (1A risk factors, 7 MD&amp;A, 8 financial
/// notes) → split each into paragraph chunks so the LLM calls stay cheap. Pure functions, no I/O.
/// </summary>
public static class FilingSections
{
    // Which SEC Items each extraction node reads. Revenue/cost breakdowns live in Item 7 (MD&A) and
    // Item 8 (segment notes); risks live in Item 1A (risk factors) + Item 7A (market risk). Ordered
    // by priority so the first item isn't starved of chunk slots downstream.
    public static string[] ItemsFor(ExtractionNode node) => node switch
    {
        ExtractionNode.RISK => ["1A", "7A"],
        _                   => ["7", "8"],   // REVENUE and COST share the financial-detail Items
    };

    private const int MaxChunkChars = 4000;        // ~1k tokens/chunk — many small, cheap calls
    private const int MaxChunksPerSection = 12;    // keep one giant section from hogging every slot
    private const int MaxChunks = 36;              // overall safety cap (≈3 sections × 12)

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
                chunks.Add(new FilingChunk($"Item {item}", chunk));
                if (++n >= MaxChunksPerSection) break;          // fair share to the next section
                if (chunks.Count >= MaxChunks) return chunks;
            }
        }
        return chunks;
    }

    /// <summary>
    /// Sequential, document-order chunks of one Item's full body — used for Item 8, whose financial
    /// tables are detached from their bold headings (so heading-based chunking mislabels and truncates
    /// them). Reuses the same Item-detection + paragraph packing as <see cref="Build"/>, but with a
    /// generous chunk budget so the dense statements/notes aren't cut short. Tables stay whole (a single
    /// table is one paragraph) unless one exceeds <see cref="MaxChunkChars"/>, which is then clipped.
    /// </summary>
    public static List<FilingChunk> BuildSection(string raw, string item, int maxChunks = 40)
    {
        var body = SectionBody(ToText(raw), item);
        if (body is null) return [];
        var chunks = new List<FilingChunk>();
        foreach (var chunk in Paragraphs(body))
        {
            chunks.Add(new FilingChunk($"Item {item}", chunk));
            if (chunks.Count >= maxChunks) break;
        }
        return chunks;
    }

    // Strip HTML to readable text, preserving paragraph/row boundaries as newlines (InnerText alone
    // merges words across tags). Plain-text filings pass through the same whitespace normalisation.
    private static string ToText(string raw)
    {
        var looksHtml = Regex.IsMatch(raw[..Math.Min(raw.Length, 2000)], "<html|<body|<div|<p|<table", RegexOptions.IgnoreCase);
        if (looksHtml)
        {
            // Force a line break where blocks close, so text nodes don't run together.
            var marked = Regex.Replace(raw, "(?i)</(p|div|tr|li|h[1-6]|table)>", "\n");
            marked = Regex.Replace(marked, "(?i)<br\\s*/?>", "\n");
            var doc = new HtmlDocument();
            doc.LoadHtml(marked);
            doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());
            raw = HtmlEntity.DeEntitize(doc.DocumentNode.InnerText) ?? "";
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
        return sb.ToString();
    }

    // The real body of "Item N": of every place that heading appears (TOC + body), take the one with
    // the most text before the next Item heading — that is the section, not the contents-page line.
    private static string? SectionBody(string text, string item)
    {
        // All "Item <n>" headings with their position, in document order. Leading markdown markers
        // (#, *, _, >) are tolerated so a sec2md heading like "# Item 1A." is found, not just plain text.
        var headings = Regex.Matches(text, @"(?im)^[#>*_\s]*Item\s+(\d+[A-Z]?)\b")
            .Select(m => (Num: m.Groups[1].Value.ToUpperInvariant(), Start: m.Index, End: m.Index + m.Length))
            .OrderBy(h => h.Start)
            .ToList();
        if (headings.Count == 0) return null;

        string? best = null;
        for (int i = 0; i < headings.Count; i++)
        {
            if (headings[i].Num != item) continue;
            var bodyStart = headings[i].End;
            var bodyEnd = i + 1 < headings.Count ? headings[i + 1].Start : text.Length;
            var body = text[bodyStart..bodyEnd].Trim();
            if (best is null || body.Length > best.Length) best = body;
        }
        return string.IsNullOrWhiteSpace(best) ? null : best;
    }

    // Pack paragraphs (blank-line separated) into <= MaxChunkChars chunks without splitting a
    // paragraph. A single oversized paragraph is clipped — UNLESS it's a table, which is emitted whole
    // (clipping a financial statement mid-table would drop rows of figures).
    private static IEnumerable<string> Paragraphs(string body)
    {
        var paras = Regex.Split(body, "\n\\s*\n").Select(p => p.Trim()).Where(p => p.Length > 0);
        var current = new StringBuilder();
        foreach (var para in paras)
        {
            if (current.Length > 0 && current.Length + para.Length > MaxChunkChars)
            {
                yield return current.ToString();
                current.Clear();
            }
            if (para.Length > MaxChunkChars)
            {
                yield return LooksLikeTable(para) ? para : para[..MaxChunkChars];
                continue;
            }
            if (current.Length > 0) current.Append("\n\n");
            current.Append(para);
        }
        if (current.Length > 0) yield return current.ToString();
    }

    // A paragraph is a table when several of its lines are markdown rows (start with '|') — used so an
    // oversized financial statement is kept whole rather than clipped.
    private static bool LooksLikeTable(string para)
    {
        int rows = 0;
        foreach (var line in para.Split('\n'))
            if (line.TrimStart().StartsWith('|') && ++rows >= 3) return true;
        return false;
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
        // sec2md feeds us markdown (no HTML markup to detect) — parse its headings line-by-line.
        // Raw SEC HTML (the sidecar-down fallback) still goes through the DOM bold-detection below.
        if (!Regex.IsMatch(raw[..Math.Min(raw.Length, 2000)], "<html|<body|<div|<p|<table", RegexOptions.IgnoreCase))
            return BuildHeadingsFromMarkdown(raw, items);

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
            // An "Item N" line is a section boundary, not a selectable sub-heading.
            var item = Regex.Match(text, @"^Item\s+(\d+[A-Z]?)\b", RegexOptions.IgnoreCase);
            if (item.Success)
            {
                Flush();
                var num = item.Groups[1].Value.ToUpperInvariant();
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

    // Markdown sibling of BuildHeadings, for the sec2md path. A sub-heading is a markdown ATX heading
    // line ("## Title") or a wholly-bold line ("**Title**"); body = the lines under it until the next
    // heading. Same Item-scoping and dedupe as the HTML path — much simpler since markdown is flat text.
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

            // An "Item N" line (any heading/bold prefix) is a section boundary, not a sub-heading.
            var item = Regex.Match(line, @"^[#>*_\s]*Item\s+(\d+[A-Z]?)\b", RegexOptions.IgnoreCase);
            if (item.Success)
            {
                Flush();
                var num = item.Groups[1].Value.ToUpperInvariant();
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
