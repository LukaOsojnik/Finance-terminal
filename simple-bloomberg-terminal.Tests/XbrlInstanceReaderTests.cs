using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Unit tests for the pure segment-cost parser (<see cref="XbrlInstanceReader.ParseSegmentCosts"/>),
/// exercised against a hand-built XBRL instance fixture — no HTTP. Covers the happy path (cost =
/// revenue − operating income), the subtraction-sanity flag (a wrong profit measure → non-reconciling),
/// and the two context kinds that must be excluded: a product-axis sub-breakdown and an off-period context.
/// </summary>
public class XbrlInstanceReaderTests
{
    // Two clean FY2023 segments (Americas, Europe), one FY2023 segment whose tagged "profit" exceeds
    // revenue (→ negative implied cost → must NOT reconcile), one product-axis context (must be
    // ignored — it's a sub-breakdown, not the segment total), and one off-period Americas context.
    private const string Fixture = """
        <xbrl>
          <context id="c-amer-2023"><entity><segment><xbrldi:explicitMember dimension="us-gaap:StatementBusinessSegmentsAxis">x:AmericasSegmentMember</xbrldi:explicitMember></segment></entity><period><startDate>2022-09-25</startDate><endDate>2023-09-30</endDate></period></context>
          <context id="c-amer-2022"><entity><segment><xbrldi:explicitMember dimension="us-gaap:StatementBusinessSegmentsAxis">x:AmericasSegmentMember</xbrldi:explicitMember></segment></entity><period><startDate>2021-09-26</startDate><endDate>2022-09-24</endDate></period></context>
          <context id="c-eur-2023"><entity><segment><xbrldi:explicitMember dimension="us-gaap:StatementBusinessSegmentsAxis">x:EuropeSegmentMember</xbrldi:explicitMember></segment></entity><period><startDate>2022-09-25</startDate><endDate>2023-09-30</endDate></period></context>
          <context id="c-bad-2023"><entity><segment><xbrldi:explicitMember dimension="us-gaap:StatementBusinessSegmentsAxis">x:JapanSegmentMember</xbrldi:explicitMember></segment></entity><period><startDate>2022-09-25</startDate><endDate>2023-09-30</endDate></period></context>
          <context id="c-amer-prod"><entity><segment><xbrldi:explicitMember dimension="us-gaap:StatementBusinessSegmentsAxis">x:AmericasSegmentMember</xbrldi:explicitMember><xbrldi:explicitMember dimension="srt:ProductOrServiceAxis">x:IPhoneMember</xbrldi:explicitMember></segment></entity><period><startDate>2022-09-25</startDate><endDate>2023-09-30</endDate></period></context>

          <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-amer-2023" decimals="-6">162560000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
          <us-gaap:OperatingIncomeLoss contextRef="c-amer-2023" decimals="-6">60508000000</us-gaap:OperatingIncomeLoss>
          <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-eur-2023" decimals="-6">94294000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
          <us-gaap:OperatingIncomeLoss contextRef="c-eur-2023" decimals="-6">36098000000</us-gaap:OperatingIncomeLoss>
          <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-bad-2023" decimals="-6">24257000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
          <us-gaap:OperatingIncomeLoss contextRef="c-bad-2023" decimals="-6">30000000000</us-gaap:OperatingIncomeLoss>
          <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-amer-2022" decimals="-6">999000000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
          <us-gaap:OperatingIncomeLoss contextRef="c-amer-2022" decimals="-6">1000000</us-gaap:OperatingIncomeLoss>
          <us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax contextRef="c-amer-prod" decimals="-6">50000000000</us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax>
          <us-gaap:OperatingIncomeLoss contextRef="c-amer-prod" decimals="-6">10000000000</us-gaap:OperatingIncomeLoss>
        </xbrl>
        """;

    [Fact]
    public void ParseSegmentCosts_PairsRevenueAndOperatingIncome_PerSegment()
    {
        var segs = XbrlInstanceReader.ParseSegmentCosts(Fixture, "2023-09-30");

        // Three FY2023 segments; the product-axis context and the off-period (2022) context are excluded.
        Assert.Equal(3, segs.Count);

        var amer = segs.Single(s => s.Segment == "Americas");
        Assert.Equal(162560000000, amer.Revenue);
        Assert.Equal(60508000000, amer.OperatingIncome);
        Assert.Equal(102052000000, amer.Cost);   // revenue − operating income
        Assert.True(amer.Reconciles);
    }

    [Fact]
    public void ParseSegmentCosts_FlagsSegmentWhereProfitExceedsRevenue()
    {
        var segs = XbrlInstanceReader.ParseSegmentCosts(Fixture, "2023-09-30");

        // Japan's tagged "profit" (30B) exceeds its revenue (24.26B) → negative implied cost → flagged.
        var bad = segs.Single(s => s.Segment == "Japan");
        Assert.True(bad.Cost < 0);
        Assert.False(bad.Reconciles);
    }

    [Fact]
    public void ParseSegmentCosts_NullPeriod_FallsBackToLatestSegmentPeriod()
    {
        // No period given → the parser targets the newest segment period present (2023-09-30), so the
        // 2022 Americas context still doesn't leak in alongside the 2023 one.
        var segs = XbrlInstanceReader.ParseSegmentCosts(Fixture, null);

        var amer = segs.Single(s => s.Segment == "Americas");
        Assert.Equal(102052000000, amer.Cost);
    }

    [Fact]
    public void ParseSegmentRevenues_ReturnsTaggedRevenue_PerSegment_NoSubtraction()
    {
        var segs = XbrlInstanceReader.ParseSegmentRevenues(Fixture, "2023-09-30");

        // Same context selection as cost: three FY2023 segments; product-axis + off-period excluded.
        Assert.Equal(3, segs.Count);

        var amer = segs.Single(s => s.Segment == "Americas");
        Assert.Equal(162560000000, amer.Revenue);   // the tagged figure, no operating-income subtraction
        Assert.Equal(94294000000, segs.Single(s => s.Segment == "Europe").Revenue);
    }
}
