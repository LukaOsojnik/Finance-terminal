namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>
/// Pure picking over SEC EDGAR <c>companyfacts</c> (<see cref="EdgarFacts"/>): given the tagged
/// us-gaap concepts, return the authoritative full-year (10-K) USD data point. Shared by the EDGAR
/// refresh (<see cref="StockService"/>, which wants the newest year) and the cost extractor's
/// grounding (<see cref="ExtractionChatService"/>, which wants the year THIS filing reported). The
/// dollar figure always comes from a tagged fact here — the LLM never transcribes it.
/// </summary>
public static class XbrlFacts
{
    // The us-gaap cost concepts the grounding surfaces, in preference order (first the filer tagged
    // wins). A company tags one of these for cost-of-revenue and, separately, an operating-expense
    // line — different filers pick different concept names, so each list is tried in order. See §4 of
    // docs/cost-extraction.md.
    public static readonly string[] Cogs = ["CostOfRevenue", "CostOfGoodsAndServicesSold", "CostsAndExpenses"];
    public static readonly string[] Opex = ["OperatingExpenses", "SellingGeneralAndAdministrativeExpense"];
    // The company-total revenue concepts the REVENUE grounding surfaces (and the cost grounding's Σ
    // check already uses), in preference order — different filers tag top-line revenue differently.
    public static readonly string[] Revenue =
        ["Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax", "RevenueFromContractWithCustomerIncludingAssessedTax"];

    // Most recent full-year (10-K) USD data point for the first concept name that has one. (Moved here
    // verbatim from StockService so the refresh path and the extractor share one picker.)
    public static EdgarFact? LatestAnnual(EdgarFacts? facts, params string[] names)
    {
        foreach (var name in names)
        {
            var p = AnnualPoints(facts, name).OrderByDescending(x => x.End).FirstOrDefault();
            if (p is not null) return p;
        }
        return null;
    }

    // The full-year (10-K) USD point whose period END matches <paramref name="end"/> — the report date
    // of the filing under extraction — for the first concept that has one, so the agent is grounded on
    // the figure THIS filing reported, not the newest on file. Falls back to <see cref="LatestAnnual"/>
    // when the period isn't present (e.g. an old filing dropped from companyfacts).
    public static EdgarFact? AnnualForEnd(EdgarFacts? facts, string? end, params string[] names)
    {
        if (!string.IsNullOrWhiteSpace(end))
            foreach (var name in names)
            {
                var p = AnnualPoints(facts, name).FirstOrDefault(x => x.End == end);
                if (p is not null) return p;
            }
        return LatestAnnual(facts, names);
    }

    // The 10-K USD data points tagged under one concept (well-formed only: a value and an end date).
    private static IEnumerable<EdgarFact> AnnualPoints(EdgarFacts? facts, string name)
    {
        if (facts?.UsGaap is null || !facts.UsGaap.TryGetValue(name, out var concept)) return [];
        if (concept.Units is null || !concept.Units.TryGetValue("USD", out var points)) return [];
        return points.Where(x => x.Form == "10-K" && x.Val.HasValue && x.End != null);
    }
}
