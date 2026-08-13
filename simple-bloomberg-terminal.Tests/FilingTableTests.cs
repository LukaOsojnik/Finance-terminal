using System.Text;
using System.Text.RegularExpressions;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Unit tests for the table-preserving half of the extraction pipeline (docs/sec-extraction.md) —
/// all pure parsers, no HTTP. Covers <see cref="FilingTables.Render"/> (structure kept, R-file chrome
/// stripped, layout tables rejected), <see cref="FilingReportReader.ParseIndex"/>,
/// <see cref="FilingSections.SelectReport"/>, and the chunking rules a financial table depends on:
/// it is never split, and it never loses the caption carrying its units.
/// </summary>
public class FilingTableTests
{
    // ── FilingTables.Render ───────────────────────────────────────────────────────────────────────

    // Shaped like a real SEC-rendered R*.htm row set: the units live in the title cell, label cells
    // name their us-gaap concept in an onclick, positives are "nump" and negatives "num" (already
    // parenthesised), and each label carries a hidden authRefData popup holding the FASB definition.
    private const string ReportTable = """
        <table class="report">
          <tr>
            <th class="tl"><div><strong>CONSOLIDATED BALANCE SHEETS - USD ($) $ in Millions</strong></div></th>
            <th class="th"><div>Sep. 30, 2023</div></th>
          </tr>
          <tr class="ro">
            <td class="pl"><a class="a" onclick="Show.showAR( this, 'defref_us-gaap_Assets', window );">Total assets</a>
              <table class="authRefData"><tr><td class="hide">X</td></tr>
                <tr><td>Definition: sum of all 999 assets reported.</td></tr></table>
            </td>
            <td class="nump">$ 352,583<span></span></td>
          </tr>
          <tr class="re">
            <td class="pl"><a class="a" onclick="Show.showAR( this, 'defref_us-gaap_RetainedEarningsAccumulatedDeficit', window );">Accumulated deficit</a></td>
            <td class="num">( 214 )<span></span></td>
          </tr>
        </table>
        """;

    [Fact]
    public void Render_KeepsRowsColumnsUnitsAndConcepts()
    {
        var html = Assert.Single(FilingTables.RenderAll(ReportTable));

        // Units ride along in the title cell — the thing a bare grid of numbers is missing.
        Assert.Contains("<th>CONSOLIDATED BALANCE SHEETS - USD ($) $ in Millions</th>", html);
        // The us-gaap concept pins an ambiguous label to the tagged fact XbrlFacts already keys on.
        Assert.Contains("<td c=\"us-gaap:Assets\">Total assets</td>", html);
        // Split "$ 352,583" and "( 214 )" cells are closed up; the negative keeps its parentheses.
        Assert.Contains("<td>$352,583</td>", html);
        Assert.Contains("<td>(214)</td>", html);
        // Every row survived, and each is one <tr> — the columns are recoverable.
        Assert.Equal(3, html.Split("<tr>").Length - 1);
    }

    [Fact]
    public void Render_DropsHiddenDefinitionPopups()
    {
        var html = Assert.Single(FilingTables.RenderAll(ReportTable));

        // The authRefData subtree is most of an R-file's bytes and none of its meaning.
        Assert.DoesNotContain("Definition", html);
        Assert.DoesNotContain("999", html);
    }

    [Fact]
    public void Render_KeepsMergedHeaderSpans()
    {
        // The structure markdown pipe tables cannot express, and the reason tables travel as HTML.
        const string merged = """
            <table>
              <tr><th></th><th colspan="2">Year Ended December 31,</th></tr>
              <tr><th></th><th>2023</th><th>2022</th></tr>
              <tr><td>Net sales</td><td>1,000</td><td>900</td></tr>
            </table>
            """;

        var html = Assert.Single(FilingTables.RenderAll(merged));

        Assert.Contains("<th colspan=\"2\">Year Ended December 31,</th>", html);
        // colspan="1" is the default and would be pure token cost on every other cell.
        Assert.DoesNotContain("colspan=\"1\"", html);
    }

    [Fact]
    public void Render_RejectsLayoutTables()
    {
        // SEC filings wrap ordinary prose in tables constantly; those must flatten to text, not
        // arrive at the model dressed as data.
        const string layout = """
            <table><tr><td>We compete in highly competitive markets.</td></tr>
                   <tr><td>Our results may fluctuate materially.</td></tr></table>
            """;

        Assert.Empty(FilingTables.RenderAll(layout));
    }

    [Fact]
    public void Render_AddsCaptionWhenGiven()
    {
        var html = Assert.Single(FilingTables.RenderAll(ReportTable, "CONSOLIDATED BALANCE SHEETS"));
        Assert.Contains("<caption>CONSOLIDATED BALANCE SHEETS</caption>", html);
    }

    // ── FilingReportReader.ParseIndex ─────────────────────────────────────────────────────────────

    // Abridged from a real Apple 10-K FilingSummary.xml, keeping one report per menu category plus
    // the XLSX-only entry (no HtmlFileName) that has to be skipped.
    private const string SummaryXml = """
        <FilingSummary>
          <MyReports>
            <Report instance="aapl-20230930.htm">
              <HtmlFileName>R1.htm</HtmlFileName><ShortName>Cover Page</ShortName><MenuCategory>Cover</MenuCategory>
            </Report>
            <Report instance="aapl-20230930.htm">
              <HtmlFileName>R3.htm</HtmlFileName><ShortName>CONSOLIDATED STATEMENTS OF OPERATIONS</ShortName><MenuCategory>Statements</MenuCategory>
            </Report>
            <Report instance="aapl-20230930.htm">
              <HtmlFileName>R5.htm</HtmlFileName><ShortName>CONSOLIDATED BALANCE SHEETS</ShortName><MenuCategory>Statements</MenuCategory>
            </Report>
            <Report instance="aapl-20230930.htm">
              <HtmlFileName>R6.htm</HtmlFileName><ShortName>CONSOLIDATED BALANCE SHEETS (Parenthetical)</ShortName><MenuCategory>Statements</MenuCategory>
            </Report>
            <Report instance="aapl-20230930.htm">
              <ShortName>All Reports</ShortName><MenuCategory>Details</MenuCategory>
            </Report>
            <Report instance="aapl-20230930.htm">
              <HtmlFileName>R60.htm</HtmlFileName><ShortName>Segment Information and Geographic Data - Net Sales (Details)</ShortName><MenuCategory>Details</MenuCategory>
            </Report>
          </MyReports>
        </FilingSummary>
        """;

    [Fact]
    public void ParseIndex_ReadsReportsAndSkipsSheetsWithNoHtmlFile()
    {
        var index = FilingReportReader.ParseIndex(SummaryXml);

        Assert.Equal(5, index.Count);                       // the HtmlFileName-less entry is dropped
        Assert.DoesNotContain(index, r => r.ShortName == "All Reports");

        var balance = index.Single(r => r.File == "R5.htm");
        Assert.Equal("CONSOLIDATED BALANCE SHEETS", balance.ShortName);
        Assert.Equal("Statements", balance.Category);
    }

    // ── FilingSections.SelectReport ───────────────────────────────────────────────────────────────

    private static List<ReportEntry> Selected(ExtractionNode node) =>
        FilingReportReader.ParseIndex(SummaryXml)
            .Where(e => FilingSections.SelectReport(e, node)).ToList();

    [Fact]
    public void SelectReport_TakesStatementsAndOnTopicDetails()
    {
        var revenue = Selected(ExtractionNode.REVENUE);

        Assert.Contains(revenue, r => r.File == "R3.htm");    // the income statement — the one kept
        Assert.Contains(revenue, r => r.File == "R60.htm");   // Details, matches "Segment"/"Net Sales"

        // The balance sheet names no revenue or cost line, and the totals it was taken for are
        // reconciled in code (ExtractionChatService.SumCheck) rather than by the model reading it.
        Assert.DoesNotContain(revenue, r => r.File == "R5.htm");
    }

    [Fact]
    public void SelectReport_SkipsCoverAndParentheticals()
    {
        var revenue = Selected(ExtractionNode.REVENUE);

        Assert.DoesNotContain(revenue, r => r.File == "R1.htm");   // Cover: entity metadata only
        Assert.DoesNotContain(revenue, r => r.File == "R6.htm");   // (Parenthetical): share counts only
    }

    [Theory]
    // Kept: the income statement, under each title filers give it — including the combined form, whose
    // "Comprehensive Income" half must not be what decides the match.
    [InlineData("CONSOLIDATED STATEMENTS OF OPERATIONS", true)]
    [InlineData("CONSOLIDATED STATEMENTS OF INCOME", true)]
    [InlineData("Consolidated Statements of Earnings", true)]
    [InlineData("Consolidated Statements of Operations and Comprehensive Income", true)]
    // Dropped: statements that name no revenue or cost line. Together these are ~half the Item 8
    // payload on Apple FY2023 and AMD FY2025 (see docs/sec-extraction.md §4).
    [InlineData("CONSOLIDATED STATEMENTS OF COMPREHENSIVE INCOME", false)]
    [InlineData("CONSOLIDATED BALANCE SHEETS", false)]
    [InlineData("CONSOLIDATED STATEMENTS OF CASH FLOWS", false)]
    [InlineData("CONSOLIDATED STATEMENTS OF SHAREHOLDERS' EQUITY", false)]
    public void SelectReport_KeepsOnlyTheIncomeStatementAmongTheStatements(string shortName, bool kept)
    {
        var entry = new ReportEntry("R9.htm", shortName, "Statements");
        Assert.Equal(kept, FilingSections.SelectReport(entry, ExtractionNode.REVENUE));
    }

    [Fact]
    public void SelectReport_TakesNothingForRisk()
    {
        // RISK reads the narrative Items 1A/7A; every rendered report is a financial statement.
        Assert.Empty(Selected(ExtractionNode.RISK));
    }

    [Fact]
    public void SelectReport_IsSelectiveEnoughToStayUnderTheFetchCap()
    {
        // The predicate runs before any download, so it is what stands between one scan and ~60 SEC
        // round-trips. Against a realistic index it must stay well inside FilingReportReader's cap.
        var index = new List<ReportEntry>
        {
            new("R1.htm", "Cover Page", "Cover"),
            new("R3.htm", "CONSOLIDATED STATEMENTS OF OPERATIONS", "Statements"),
            new("R5.htm", "CONSOLIDATED BALANCE SHEETS", "Statements"),
            new("R9.htm", "Summary of Significant Accounting Policies", "Notes"),
            new("R30.htm", "Revenue (Tables)", "Tables"),
            new("R41.htm", "Revenue - Additional Information (Details)", "Details"),
            new("R42.htm", "Revenue - Net Sales Disaggregated by Significant Products (Details)", "Details"),
            new("R50.htm", "Income Taxes - Provision for Income Taxes (Details)", "Details"),
            new("R55.htm", "Leases - Lease Liability Maturities (Details)", "Details"),
            new("R60.htm", "Segment Information and Geographic Data - Net Sales (Details)", "Details"),
        };

        var picked = index.Where(e => FilingSections.SelectReport(e, ExtractionNode.REVENUE)).ToList();

        Assert.Equal(
            ["R3.htm", "R42.htm", "R60.htm"],
            picked.Select(p => p.File));
    }

    // ── Chunking: a table must stay whole, and keep its units ─────────────────────────────────────

    [Fact]
    public void Build_KeepsTableWholeAndWithItsCaption()
    {
        // Enough prose before the table to push it past the chunk budget, so the caption and the
        // table would land in different worker calls unless the caption is deliberately carried over.
        var filler = new StringBuilder();
        for (int i = 0; i < 40; i++)
            filler.Append("<p>Macroeconomic conditions have impacted the Company's results in period ")
                  .Append(i).Append(" and could continue to do so materially going forward.</p>\n");

        var raw = $"""
            <html><body>
            <p>Item 7. Management's Discussion and Analysis</p>
            {filler}
            <p>The following table shows net sales by reportable segment (dollars in millions):</p>
            <table>
              <tr><td></td><td>2023</td><td>2022</td></tr>
              <tr><td>Americas</td><td>$ 162,560</td><td>$ 169,658</td></tr>
              <tr><td>Europe</td><td>94,294</td><td>95,118</td></tr>
              <tr><td>Total net sales</td><td>$ 383,285</td><td>$ 394,328</td></tr>
            </table>
            <p>Item 8. Financial Statements</p>
            </body></html>
            """;

        var chunks = FilingSections.Build(raw, ["7"]);

        // Exactly one chunk holds the table, and it holds all of it — a statement split across two
        // calls loses rows of figures, which is the failure this whole path exists to prevent.
        var withTable = Assert.Single(chunks.Where(c => c.Text.Contains("<table>")));
        Assert.Contains("Americas", withTable.Text);
        Assert.Contains("Europe", withTable.Text);
        Assert.Contains("$383,285", withTable.Text);
        Assert.Contains("</table>", withTable.Text);

        // …and the sentence carrying the scale came with it.
        Assert.Contains("dollars in millions", withTable.Text);
    }

    [Fact]
    public void Render_FoldsCurrencyAndParenCellsIntoTheFigure()
    {
        // Intel lays its segment tables out with the currency symbol in its own cell, which reads as
        // a separate (meaningless) column; negatives split their parentheses off the same way.
        const string split = """
            <table>
              <tr><td>Year Ended</td><td></td><td>CCG</td></tr>
              <tr><td>Revenue</td><td></td><td>$</td><td>32,228</td></tr>
              <tr><td>Net loss</td><td></td><td>(</td><td>214</td><td>)</td></tr>
            </table>
            """;

        var html = Assert.Single(FilingTables.RenderAll(split));

        Assert.Contains("<td>$32,228</td>", html);
        Assert.Contains("<td>(214)</td>", html);
        Assert.DoesNotContain("<td>$</td>", html);
    }

    // 3M FY2025 Item 7, abridged: a three-column segment table the filer lays out across 33 grid
    // columns of spacers, with the currency symbol and the percent sign each in their own cell.
    private const string SpacerTable = """
        <table>
          <tr><td colspan="3">Safety and Industrial Business (45.6% of consolidated sales):</td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="3"></td><td colspan="3"></td></tr>
          <tr><td colspan="3"></td><td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3">2025</td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td><td colspan="3">2024</td></tr>
          <tr><td colspan="3">Sales (millions)</td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td>$</td><td colspan="2">11,384</td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="2"></td>
              <td>$</td><td colspan="2">10,961</td></tr>
          <tr><td colspan="3">Organic sales(b)</td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="2">3.2</td><td>%</td>
              <td colspan="3"></td><td colspan="3"></td><td colspan="3"></td>
              <td colspan="2">0.7</td><td>%</td></tr>
        </table>
        """;

    [Fact]
    public void Render_CollapsesSpacerColumnsButKeepsTheDataColumns()
    {
        var html = Assert.Single(FilingTables.RenderAll(SpacerTable));

        // 33 presentational columns carry three columns of meaning. Every spacer the model would have
        // had to read past to match a figure to its year is gone.
        Assert.DoesNotContain("<td></td><td></td>", html);
        Assert.True(html.Length < SpacerTable.Length / 2, $"expected a large shrink, got:\n{html}");

        // The percent sign is the figure's unit, not a column of its own.
        Assert.Contains("<td>3.2%</td>", html);
        Assert.DoesNotContain("<td>%</td>", html);
    }

    [Fact]
    public void Render_KeepsEachFigureUnderItsOwnYear()
    {
        var html = Assert.Single(FilingTables.RenderAll(SpacerTable));

        // The header row and the money row must agree on where a column starts, or the model reads
        // 2025 sales out of the 2024 column. The "$" sitting in its own cell is what used to break
        // this: dropping it dropped its column too, sliding every figure right of it one column left.
        var year = Cells(Row(html, "2025"));
        var sales = Cells(Row(html, "11,384"));
        Assert.Equal(year.IndexOf("2025"), sales.IndexOf("$11,384"));
        Assert.Equal(year.IndexOf("2024"), sales.IndexOf("$10,961"));

        // …and the same alignment holds for a row that never had a currency symbol.
        var organic = Cells(Row(html, "Organic"));
        Assert.Equal(year.IndexOf("2025"), organic.IndexOf("3.2%"));
        Assert.Equal(year.IndexOf("2024"), organic.IndexOf("0.7%"));
    }

    // ── Which Items the revenue workers are handed ────────────────────────────────────────────────

    [Theory]
    [InlineData("10-K")]
    [InlineData("10-K/A")]
    [InlineData(null)]        // form unknown → the annual routing, which is the common case
    public void ItemsFor_RoutesTheAnnualRevenueItems(string? form)
    {
        var items = FilingSections.ItemsFor(ExtractionNode.REVENUE, form);

        // Item 1 for the customer / channel / backlog / JV narrative, Item 1A for concentration named
        // as a risk, Item 7 for the drivers, Item 8 for the notes that carry the actual breakdowns
        // (ASC 280 / 606 / 275-10-50 / 323).
        Assert.Equal(["1", "1A", "7", "8"], items);
        // Item 5 is repurchase and dividend context. It is not revenue, and it stays out.
        Assert.DoesNotContain("5", items);
    }

    [Fact]
    public void Build_GivesEveryRoutedItemItsShareOfTheChunkBudget()
    {
        // The overall cap is checked AFTER the per-section break, so unless it is sized to the widest
        // routing the LAST Item gets a single chunk and Build returns. Item 8 is last in document
        // order and is the Item carrying the figures, so it is exactly the one a stale cap starves.
        var items = FilingSections.ItemsFor(ExtractionNode.REVENUE, "10-K");

        // Paragraphs are PACKED up to MaxChunkChars before a chunk is cut, so a section of short
        // paragraphs is one chunk no matter how many it has. Each paragraph here is over half the
        // budget, so no two fit together and every paragraph becomes a chunk of its own.
        var para = new string('x', FilingSections.MaxChunkChars / 2 + 100);
        var filing = new StringBuilder();
        foreach (var item in items)
        {
            filing.Append("Item ").Append(item).Append(". Section heading.\n\n");
            for (var i = 0; i < 15; i++) filing.Append(para).Append("\n\n");   // > the per-section cap
        }

        var chunks = FilingSections.Build(filing.ToString(), items);
        var perItem = items.ToDictionary(i => i, i => chunks.Count(c => c.Item == $"Item {i}"));

        // Every routed Item is represented, and all four get the SAME share — none is cut short
        // because the Items ahead of it in document order spent the whole budget first.
        Assert.All(perItem, kv =>
            Assert.True(kv.Value > 1, $"Item {kv.Key} was starved: {kv.Value} chunk(s)"));
        Assert.Single(perItem.Values.Distinct());
    }

    [Theory]
    [InlineData("8-K")]
    [InlineData("8-K/A")]
    public void ItemsFor_RoutesTheCurrentReportItems(string form)
    {
        // An 8-K numbers on its own scheme, so the annual Items would find nothing in one.
        Assert.Equal(["1.01", "2.02", "8.01"], FilingSections.ItemsFor(ExtractionNode.REVENUE, form));
    }

    [Fact]
    public void ItemsFor_LeavesCostAndRiskOnTheirAnnualItems()
    {
        Assert.Equal(["1", "7", "8"], FilingSections.ItemsFor(ExtractionNode.COST, "8-K"));
        Assert.Equal(["1A", "7A"], FilingSections.ItemsFor(ExtractionNode.RISK, "8-K"));
    }

    // A current report, shaped like the real thing: the two Items revenue wants (1.01, 2.02, 8.01)
    // interleaved with two it does not (5.02 officer changes, 9.01 exhibit list).
    private const string CurrentReport = """
        Item 1.01 Entry into a Material Definitive Agreement.

        On March 3, 2026, the Company entered into a multi-year supply agreement with Northwind Traders.

        Item 2.02 Results of Operations and Financial Condition.

        Revenue for the fourth quarter was $1,204 million, up 12% year over year.

        Item 5.02 Departure of Directors or Certain Officers.

        Jane Doe resigned as Chief Accounting Officer effective March 31, 2026.

        Item 8.01 Other Events.

        The Company announced a new distribution agreement covering the EMEA region.

        Item 9.01 Financial Statements and Exhibits.

        Exhibit 99.1 Press release dated March 3, 2026.
        """;

    [Fact]
    public void Build_ReadsDottedItemNumbersOutOfACurrentReport()
    {
        var chunks = FilingSections.Build(
            CurrentReport, FilingSections.ItemsFor(ExtractionNode.REVENUE, "8-K"));

        // The decimal must survive detection. Truncated to whole numbers ("2.02" -> "2") every Item
        // collapses onto its prefix, no requested Item matches, and the scan silently returns nothing.
        Assert.Equal(["Item 1.01", "Item 2.02", "Item 8.01"], chunks.Select(c => c.Item));

        var results = chunks.Single(c => c.Item == "Item 2.02");
        Assert.Contains("$1,204 million", results.Text);
        // 5.02 is a different Item, so it ends 2.02's body rather than being swallowed into it.
        Assert.DoesNotContain("Jane Doe", results.Text);
        Assert.DoesNotContain(chunks, c => c.Text.Contains("Jane Doe"));
    }

    [Fact]
    public void Build_StillReadsWholeNumberItemsOutOfAnAnnualReport()
    {
        // The regression guard for the decimal group: a trailing "." with no digits after it is not
        // part of the number, so 10-K detection is exactly as it was.
        const string annual = """
            Item 7. Management's Discussion and Analysis of Financial Condition.

            Net sales increased 8% in fiscal 2026, led by the Services segment.

            Item 7A. Quantitative and Qualitative Disclosures About Market Risk.

            We are exposed to interest rate risk on our investment portfolio.
            """;

        var mdna = Assert.Single(FilingSections.Build(annual, ["7"]));

        Assert.Equal("Item 7", mdna.Item);
        Assert.Contains("Net sales increased 8%", mdna.Text);
        Assert.DoesNotContain("interest rate risk", mdna.Text);
    }

    private static string Row(string html, string marker) =>
        html.Split("<tr>").Single(r => r.Contains(marker));

    // Cell texts expanded so a colspan occupies that many slots — the same grid the model reads off
    // the markup, so an index into it is the column a figure appears in.
    private static List<string> Cells(string row)
    {
        var slots = new List<string>();
        foreach (Match m in Regex.Matches(row, @"<t[dh](?:\s+colspan=""(\d+)"")?[^>]*>(.*?)</t[dh]>"))
            for (var i = 0; i < (m.Groups[1].Success ? int.Parse(m.Groups[1].Value) : 1); i++)
                slots.Add(m.Groups[2].Value);
        return slots;
    }

    [Fact]
    public void Build_FindsSectionByCanonicalTitle_WhenFilerOmitsTheItemNumber()
    {
        // Intel-shaped: "Item 1A." appears ONLY in the contents table (which renders as a table, so
        // it is not a line start), the body heads the section "Risk Factors", and that title repeats
        // as a running page header — which must not cut the section at its first page break.
        var pages = new StringBuilder();
        for (int p = 0; p < 4; p++)
            pages.Append("<p>Risk Factors</p>")
                 .Append($"<p>Supply chain disruption could materially affect results, page {p}.</p>");

        var raw = $"""
            <html><body>
            <table><tr><td>Item 1A.</td><td>Risk Factors</td><td>37</td></tr>
                   <tr><td>Item 7.</td><td>Management's Discussion and Analysis</td><td>52</td></tr></table>
            <p>Management's Discussion and Analysis</p>
            <p>Revenue grew 12% during the period on strong demand.</p>
            {pages}
            <p>Other Key Information</p>
            </body></html>
            """;

        var risk = string.Join("\n", FilingSections.Build(raw, ["1A"]).Select(c => c.Text));
        Assert.Contains("Supply chain disruption", risk);
        Assert.Contains("page 3", risk);                    // survived the running headers

        var mdna = string.Join("\n", FilingSections.Build(raw, ["7"]).Select(c => c.Text));
        Assert.Contains("Revenue grew 12%", mdna);
        Assert.DoesNotContain("Supply chain disruption", mdna);   // and stopped at the next section
    }

    [Fact]
    public void Build_DoesNotRunTableCellsTogether()
    {
        // The old ToText() took InnerText over a <table>, putting no delimiter between <td>s, so a
        // row arrived as "Americas$162,560$169,658" — the columns were simply gone.
        const string raw = """
            <html><body>
            <p>Item 7. Management's Discussion and Analysis</p>
            <table>
              <tr><td>Americas</td><td>$ 162,560</td><td>$ 169,658</td></tr>
              <tr><td>Europe</td><td>94,294</td><td>95,118</td></tr>
            </table>
            <p>Item 8. Financial Statements</p>
            </body></html>
            """;

        var text = string.Join("\n", FilingSections.Build(raw, ["7"]).Select(c => c.Text));

        Assert.DoesNotContain("Americas$162,560", text);
        Assert.Contains("<td>Americas</td><td>$162,560</td>", text);
    }
}
