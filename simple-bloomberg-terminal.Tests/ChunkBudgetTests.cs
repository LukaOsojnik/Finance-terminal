using System.Text;
using simple_bloomberg_terminal.Models.Enums;
using Xunit.Abstractions;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// The chunk-distribution half of the scan: how many worker calls one filing costs, and whether the
/// chunks that survive a budget are the ones carrying figures.
///
/// The scan assembles its chunk list from three independent feeds (ScanAutoAsync):
///   A. triaged bold headings for the narrative Items          — PackHeadings
///   B. Item 8, the SEC's rendered statement tables            — BuildReports
///   C. any narrative Item whose heading outline was too thin  — BuildSection, 40 chunks, untriaged
/// Feed C is the one that has no relevance filter and no share of a common budget, so these tests
/// measure it directly and then measure the three feeds together.
/// </summary>
public class ChunkBudgetTests
{
    private readonly ITestOutputHelper _out;

    public ChunkBudgetTests(ITestOutputHelper output) => _out = output;

    // ── Synthetic filings ─────────────────────────────────────────────────────────────────────────

    // A segment revenue table, shaped like the real disaggregation note: row labels naming segments,
    // a units-bearing caption, and figures. This is the single most valuable paragraph in an MD&A for
    // the REVENUE node, and in a real filing it sits LATE in the section.
    private const string SegmentTable = """
        <table>
          <tr><th>Net sales by reportable segment - USD ($) $ in Millions</th><th>2026</th><th>2025</th></tr>
          <tr><td>Data Center</td><td>12,580</td><td>9,361</td></tr>
          <tr><td>Client</td><td>7,043</td><td>6,212</td></tr>
          <tr><td>Gaming</td><td>2,588</td><td>3,774</td></tr>
        </table>
        """;

    // Boilerplate MD&A prose: no figures, no segment words. Sized just over half the chunk budget so
    // no two paragraphs pack together — one paragraph is one chunk, which makes the counts below
    // arithmetic rather than guesswork.
    private static string Boilerplate(int n) =>
        $"Paragraph {n}. " + new string('x', FilingSections.MaxChunkChars / 2 + 100);

    /// <summary>
    /// An Item 7 with no bold headings at all — the Intel shape that sends the Item down feed C.
    /// <paramref name="tableAt"/> is the paragraph index the segment table is placed at.
    /// </summary>
    private static string ThinMdna(int paragraphs, int tableAt)
    {
        // Every block on its own source line, the way EDGAR emits it. Without the line breaks ToText
        // collapses the whole prose run into ONE paragraph (it keeps at most one blank line, and a
        // bare </p> yields only a single newline), and Paragraphs then clips it to a single chunk.
        var sb = new StringBuilder("<html>\n<body>\n");
        sb.Append("<p>Item 7. Management's Discussion and Analysis of Financial Condition.</p>\n");
        for (var i = 0; i < paragraphs; i++)
        {
            if (i == tableAt) sb.Append(SegmentTable).Append('\n');
            sb.Append("<p>").Append(Boilerplate(i)).Append("</p>\n");
        }
        sb.Append("<p>Item 8. Financial Statements and Supplementary Data.</p>\n");
        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // Document-order truncation: what BuildSection did before ranking. Kept here as the baseline the
    // assertions below compare against, so the tests state a DIFFERENCE rather than a bare number.
    private static List<FilingChunk> FirstN(string raw, string item, int take) =>
        FilingSections.BuildSection(raw, item, ExtractionNode.REVENUE, int.MaxValue).Take(take).ToList();

    // ── Ranking: which chunks survive the cut ─────────────────────────────────────────────────────

    [Fact]
    public void RankedTruncation_KeepsTheSegmentTableThatDocumentOrderDropped()
    {
        // The table sits at paragraph 50 of 60 — past a 40-chunk cut, which is where a real
        // disaggregation note sits relative to the MD&A prose that precedes it.
        var raw = ThinMdna(60, tableAt: 50);

        var before = FirstN(raw, "7", 40);
        var after = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 40);

        _out.WriteLine($"segment table kept — document order: {before.Any(c => c.Text.Contains("Data Center"))}");
        _out.WriteLine($"segment table kept — ranked:         {after.Any(c => c.Text.Contains("Data Center"))}");

        Assert.DoesNotContain(before, c => c.Text.Contains("Data Center"));
        Assert.Contains(after, c => c.Text.Contains("Data Center"));
    }

    [Fact]
    public void RankedTruncation_SurvivesEvenAtASixthOfTheBudget()
    {
        // The budget a thin Item actually gets once Item 8 and the triaged headings have been paid
        // for (MinChunksPerThinItem). Document order at this width keeps the first six paragraphs of
        // boilerplate; ranking still finds the one paragraph with figures in it.
        var raw = ThinMdna(60, tableAt: 50);
        var after = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 6);

        Assert.Equal(6, after.Count);
        Assert.Contains(after, c => c.Text.Contains("Data Center"));
    }

    [Fact]
    public void RankedTruncation_HandsTheSurvivorsBackInDocumentOrder()
    {
        // Ranking decides WHICH chunks are read, not what order the worker reads them in — a filing
        // read back-to-front would make the digest incoherent for the lead analyst.
        var raw = ThinMdna(60, tableAt: 50);
        var kept = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 10);

        var all = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, int.MaxValue);
        var positions = kept.Select(k => all.FindIndex(a => a.Text == k.Text)).ToList();

        Assert.Equal(positions.OrderBy(p => p), positions);
    }

    [Fact]
    public void RankedTruncation_IsANoOpWhenTheSectionFitsTheBudget()
    {
        // Most filings never hit the cap. They must come through byte-identical and in filing order —
        // the change is only allowed to alter behaviour on the sections that were being truncated.
        var raw = ThinMdna(10, tableAt: 5);

        var ranked = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 40);
        var plain = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, int.MaxValue);

        Assert.Equal(plain.Select(c => c.Text), ranked.Select(c => c.Text));
    }

    // ── The shared ceiling across the three feeds ─────────────────────────────────────────────────

    [Theory]
    [InlineData(1)]   // the Intel shape: one Item's outline undetectable
    [InlineData(3)]   // the worst case: every narrative Item thin, feed A contributes nothing
    public void SharedCeiling_BoundsTheWorkerCountRegardlessOfHowManyItemsAreThin(int thinItems)
    {
        const int feedAandB = 10;   // a typical Item 8 after SelectReport, per the AMD/Apple measurements in the source
        var raw = ThinMdna(60, tableAt: 50);

        // Before: each thin Item independently took BuildSection's default 40.
        var before = feedAandB + 40 * thinItems;

        // After: the thin feed splits whatever the ceiling has left, with a floor per Item.
        var remaining = Math.Max(0, FilingSections.MaxScanChunks - feedAandB);
        var perItem = Math.Max(6, remaining / thinItems);   // MinChunksPerThinItem
        var after = feedAandB + Enumerable.Range(0, thinItems)
            .Sum(_ => FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, perItem).Count);

        _out.WriteLine($"{thinItems} thin Item(s): {before} calls ({Math.Ceiling(before / 6.0)} rounds) " +
                       $"→ {after} calls ({Math.Ceiling(after / 6.0)} rounds)");

        Assert.True(after < before, $"expected a reduction, got {before} → {after}");
        Assert.True(after <= FilingSections.MaxScanChunks,
            $"the ceiling is {FilingSections.MaxScanChunks} but the scan planned {after} calls");
    }

    [Fact]
    public void SharedCeiling_NeverStarvesAThinItemToNothing()
    {
        // Item 8 alone can eat the whole ceiling on a filing with many rendered reports. A thin Item 7
        // must still be looked at rather than dropped from the scan without trace.
        var raw = ThinMdna(60, tableAt: 50);
        var remaining = Math.Max(0, FilingSections.MaxScanChunks - 48);
        var perItem = Math.Max(6, remaining / 1);

        var chunks = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, perItem);

        Assert.Equal(6, chunks.Count);
        Assert.Contains(chunks, c => c.Text.Contains("Data Center"));
    }

    // ── Node awareness ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Ranking_FollowsTheNode()
    {
        // The same section ranked for two nodes must not produce the same pick, or the keyword lists
        // are doing nothing. A supplier paragraph is COST's; a customer paragraph is REVENUE's.
        var raw = new StringBuilder("<html>\n<body>\n<p>Item 7. Management's Discussion and Analysis.</p>\n")
            .Append("<p>").Append(Boilerplate(0)).Append("</p>\n")
            .Append("<p>We depend on a single supply agreement for wafer purchase obligations, and our ")
            .Append("cost of revenue rose accordingly. ").Append(new string('y', 2000)).Append("</p>\n")
            .Append("<p>").Append(Boilerplate(1)).Append("</p>\n")
            .Append("<p>Our largest customer accounted for a concentration of net sales in the EMEA ")
            .Append("geographic region. ").Append(new string('z', 2000)).Append("</p>\n")
            .Append("<p>Item 8. Financial Statements and Supplementary Data.</p>\n</body>\n</html>")
            .ToString();

        var forCost = FilingSections.BuildSection(raw, "7", ExtractionNode.COST, 1);
        var forRevenue = FilingSections.BuildSection(raw, "7", ExtractionNode.REVENUE, 1);

        Assert.Contains("wafer purchase obligations", forCost.Single().Text);
        Assert.Contains("largest customer", forRevenue.Single().Text);
    }
}
