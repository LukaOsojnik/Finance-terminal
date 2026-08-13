using System.Text;
using System.Text.RegularExpressions;
using HtmlAgilityPack;

namespace simple_bloomberg_terminal.Services.Extraction;

/// <summary>
/// Renders a filing's HTML tables in a form an LLM can still read the columns of.
///
/// The old pipeline flattened every table to a markdown pipe grid, which cannot express the merged
/// header cells SEC tables are built on ("Year Ended December 31," spanning three year columns), and
/// then <c>FilingSections.ToText</c> dropped even the pipes — cells ran together with no delimiter at
/// all. Here a table stays a table: minimal HTML with <c>colspan</c>/<c>rowspan</c> intact, and
/// everything that is only presentation (styles, classes, anchors, the R-files' hidden definition
/// popups) removed so the structure costs few tokens.
///
/// Pure functions, no I/O — same contract as <see cref="FilingSections"/>.
/// </summary>
public static class FilingTables
{
    /// <summary>Cells below this count make a table a layout wrapper, not data — SEC HTML nests
    /// single-cell tables purely for spacing and those must not reach the model as tables.</summary>
    private const int MinCells = 4;

    /// <summary>A rendered table is one line per row, with no blank lines inside, so
    /// <see cref="FilingSections"/> sees the whole table as a single paragraph and never splits it
    /// across two worker calls.</summary>
    public static bool IsTableBlock(string paragraph) =>
        paragraph.StartsWith("<table", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// One HTML table as compact structure-preserving HTML, or <c>null</c> when the node is a layout
    /// wrapper rather than data. <paramref name="caption"/> is emitted as a <c>&lt;caption&gt;</c> so the
    /// table carries its own title/units even after chunking moves it away from the surrounding prose.
    /// </summary>
    public static string? Render(HtmlNode table, string? caption = null)
    {
        // The R-files ship a hidden authRefData table per concept holding the full FASB definition —
        // 100kB of prose we never want. Drop those subtrees before counting or rendering anything.
        foreach (var junk in table.SelectNodes(".//table[contains(@class,'authRefData')]|.//script|.//style")
                             ?.ToList() ?? [])
            junk.Remove();

        var rows = table.SelectNodes(".//tr")?.ToList() ?? [];
        if (rows.Count < 2) return null;

        // Pass 1 — resolve every cell to where it actually sits in the filer's grid.
        var grid = new List<List<Cell>>();
        foreach (var tr in rows)
        {
            // Only this row's own cells: a nested table's cells belong to the nested table.
            var cellNodes = tr.SelectNodes("./td|./th")?.ToList() ?? [];
            if (cellNodes.Count == 0) continue;

            var row = new List<Cell>();
            var col = 0;
            foreach (var cell in cellNodes)
            {
                if (cell.GetAttributeValue("class", "").Contains("hide")) continue;   // R-file popup chrome
                var span = Math.Max(1, cell.GetAttributeValue("colspan", 1));
                row.Add(new Cell(
                    cell,
                    cell.Name.Equals("th", StringComparison.OrdinalIgnoreCase) ? "th" : "td",
                    Clean(cell.InnerText),
                    col,
                    span));
                col += span;
            }
            FoldTypographyCells(row);
            grid.Add(row);
        }

        // Pass 2 — keep only the columns a value actually starts in. Filers lay a three-column segment
        // table out across thirty-odd grid columns of pure spacing, and every one of those empty
        // columns is a column the model has to read PAST to match a figure to its header. Wide tables
        // are exactly where table comprehension collapses (MMTU's cell-lookup accuracy falls from 0.81
        // at 5 columns to 0.43 at 50), so presentational width is not free structure — it is the most
        // expensive kind of noise this pipeline can hand a model.
        //
        // A column is worth keeping only where a value starts.
        var starts = grid.SelectMany(r => r)
            .Where(c => !c.Dropped && c.Text.Length > 0)
            .Select(c => c.Start)
            .Distinct().Order().ToList();
        if (starts.Count == 0) return null;

        // …and two of those are really ONE column unless some row puts distinct values in both.
        // Filers are not consistent about which column a figure's "$" sits in — 3M puts the 2025 one
        // inside that year's header span and the 2024 one in the gutter to its left — so geometry
        // alone cannot say whether two nearby starts are two columns or one. The rows can: a genuine
        // two-year header has a row holding 2023 AND 2022, whereas nothing in the filing ever holds a
        // value in both the gutter and the column beside it.
        var separable = new HashSet<int>();
        foreach (var row in grid)
        {
            var used = row.Where(c => !c.Dropped && c.Text.Length > 0).Select(c => c.Start).ToHashSet();
            for (var i = 0; i + 1 < starts.Count; i++)
                if (used.Contains(starts[i]) && used.Contains(starts[i + 1])) separable.Add(i);
        }

        var slotOf = new Dictionary<int, int> { [starts[0]] = 0 };
        for (int i = 1, slot = 0; i < starts.Count; i++)
            slotOf[starts[i]] = separable.Contains(i - 1) ? ++slot : slot;

        // Pass 3 — emit against the surviving columns.
        var rendered = new List<string>();
        int cells = 0, numeric = 0;
        foreach (var row in grid)
        {
            var live = row.Where(c => !c.Dropped && c.Text.Length > 0).ToList();
            if (live.Count == 0) continue;

            var sb = new StringBuilder("<tr>");
            var slot = 0;
            foreach (var cell in live)
            {
                var at = slotOf[cell.Start];
                if (at < slot) continue;                                  // absorbed by a folded neighbour
                for (; slot < at; slot++) sb.Append("<td></td>");         // a column this row skips

                // How many surviving columns this cell covers — a merged header keeps its span, a label
                // stretched over spacer columns loses the ones that carried nothing.
                var span = starts.Where(s => s >= cell.Start && s < cell.Start + cell.Span)
                    .Select(s => slotOf[s]).Distinct().Count();

                sb.Append('<').Append(cell.Tag);
                if (span > 1) sb.Append(" colspan=\"").Append(span).Append('"');
                Span(sb, cell.Node, "rowspan");
                // R-file label cells name their us-gaap concept in the onclick handler
                // ("defref_us-gaap_CashAndCashEquivalentsAtCarryingValue"). That is the single most
                // useful thing on the row — it pins an ambiguous label to a canonical tagged concept —
                // so it is the one attribute worth its tokens.
                if (Concept(cell.Node) is { } concept) sb.Append(" c=\"").Append(concept).Append('"');
                sb.Append('>').Append(cell.Text).Append("</").Append(cell.Tag).Append('>');

                cells++;
                if (LooksNumeric(cell.Text)) numeric++;
                slot = at + span;
            }
            sb.Append("</tr>");
            rendered.Add(sb.ToString());
        }

        // A data table has enough cells to be worth the markup, and at least one number — a table of
        // pure prose is a layout device (SEC filings wrap paragraphs in tables constantly).
        if (rendered.Count < 2 || cells < MinCells || numeric == 0) return null;

        var head = caption is { Length: > 0 } ? $"<caption>{Clean(caption)}</caption>" : "";
        return "<table>" + head + string.Join("", rendered) + "</table>";
    }

    /// <summary>Every data table in a document, in document order, already rendered. Used for the SEC
    /// R-files, where the whole file is one statement table.</summary>
    public static List<string> RenderAll(string html, string? caption = null)
    {
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        doc.DocumentNode.SelectNodes("//script|//style|//head")?.ToList().ForEach(n => n.Remove());

        var result = new List<string>();
        // Outermost tables only: Render already walks a table's own rows, and a nested layout table
        // would otherwise be emitted twice (once inside its parent, once on its own).
        foreach (var t in doc.DocumentNode.SelectNodes("//table")?.ToList() ?? [])
        {
            if (t.Ancestors("table").Any()) continue;
            if (Render(t, caption) is { } rendered) result.Add(rendered);
        }
        return result;
    }

    /// <summary>A cell being drafted. <see cref="Text"/>, <see cref="Start"/> and <see cref="Span"/> are
    /// mutable so a currency symbol sitting in its own cell can be folded into the figure it belongs
    /// to — taking its column with it.</summary>
    private sealed class Cell(HtmlNode node, string tag, string text, int start, int span)
    {
        public HtmlNode Node { get; } = node;
        public string Tag { get; } = tag;
        public string Text { get; set; } = text;
        /// <summary>First column of the filer's grid this cell occupies.</summary>
        public int Start { get; set; } = start;
        /// <summary>How many grid columns it occupies (its colspan).</summary>
        public int Span { get; set; } = span;
        public bool Dropped { get; set; }
    }

    /// <summary>
    /// Fold cells that hold only typography into the figure they decorate. Filers lay financial
    /// tables out with the currency symbol and the parentheses of a negative in their OWN cells —
    /// Intel's segment table emits <c>&lt;td&gt;$&lt;/td&gt;&lt;td&gt;32,228&lt;/td&gt;</c> — which
    /// reads to a model as two columns, one of them meaningless.
    ///
    /// A folded cell hands its COLUMNS to the figure as well as its text. Dropping the column too is
    /// what silently slid every figure to the right of a "$" one column left of its header — on a 3M
    /// segment table that put the 2024 sales figure under no header at all, on precisely the two rows
    /// carrying the money.
    ///
    /// Genuinely empty spacer cells are left for <see cref="Render"/> to resolve: they carry no value,
    /// so no column anchors on them and they cost nothing in the output.
    /// </summary>
    private static void FoldTypographyCells(List<Cell> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (row[i].Dropped) continue;

            if (row[i].Text is "$" or "(")
            {
                // Belongs to the next figure: "$" + "32,228" -> "$32,228".
                var j = row.FindIndex(i + 1, c => !c.Dropped && c.Text.Length > 0);
                if (j < 0) continue;
                row[j].Text = row[i].Text + row[j].Text;
                row[j].Span += row[j].Start - row[i].Start;   // absorb the columns it sat in front of
                row[j].Start = row[i].Start;
                row[i].Dropped = true;
            }
            // ")" closes the previous figure's open paren ("(214" + ")" -> "(214)"); a lone "%" is the
            // unit of the figure to its left ("3.2" + "%" -> "3.2%"), which filers put in its own cell
            // just as often and which reads as an entire extra column of nothing but percent signs.
            else if (row[i].Text is ")" or "%" && i > 0)
            {
                var j = row.FindLastIndex(i - 1, c => !c.Dropped && c.Text.Length > 0);
                if (j < 0) continue;
                row[j].Text += row[i].Text;
                row[j].Span = row[i].Start + row[i].Span - row[j].Start;
                row[i].Dropped = true;
            }
        }
    }

    // Copy colspan/rowspan only when they actually span — "colspan=1" on every cell is pure token cost.
    private static void Span(StringBuilder sb, HtmlNode cell, string attr)
    {
        var v = cell.GetAttributeValue(attr, 1);
        if (v > 1) sb.Append(' ').Append(attr).Append("=\"").Append(v).Append('"');
    }

    // "Show.showAR( this, 'defref_us-gaap_Assets', window )" -> "us-gaap:Assets". R-files only.
    private static string? Concept(HtmlNode cell)
    {
        var onclick = cell.SelectSingleNode(".//a[@onclick]")?.GetAttributeValue("onclick", "") ?? "";
        var m = Regex.Match(onclick, @"defref_([A-Za-z0-9-]+)_([A-Za-z0-9]+)");
        return m.Success ? $"{m.Groups[1].Value}:{m.Groups[2].Value}" : null;
    }

    // Cell text ready for the prompt: entities decoded, non-breaking spaces and runs of whitespace
    // collapsed, and the split "$ 1,234" / "( 214 )" artefacts closed up — the same normalisation the
    // Python sidecar's regex pass used to do, applied at the source cell instead of after the fact.
    private static string Clean(string raw)
    {
        var s = HtmlEntity.DeEntitize(raw) ?? "";
        s = s.Replace(' ', ' ').Replace('’', '\'').Replace('“', '"').Replace('”', '"');
        s = Regex.Replace(s, @"\s+", " ").Trim();
        s = Regex.Replace(s, @"\(\s+(?=[\d$])", "(");
        s = Regex.Replace(s, @"(?<=[\d%)])\s+\)", ")");
        // Only when a figure follows — a units caption ("$ in Millions") must keep its space.
        s = Regex.Replace(s, @"\$\s+(?=[\d(])", "$");
        // The markup is our column delimiter now, so a literal '<' in cell text would corrupt it.
        return s.Replace("<", "&lt;").Replace(">", "&gt;");
    }

    // A cell counts as numeric if it carries digits and reads like a figure rather than a sentence.
    private static bool LooksNumeric(string text) =>
        text.Length > 0 && text.Length < 40 && text.Any(char.IsDigit) &&
        Regex.IsMatch(text, @"^[\$\(\)\-\s0-9,.%]+$");
}
