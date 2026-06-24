using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// One company's membership in one <see cref="StockIndex"/> — the N:M join between StockIndex and
/// Company, carrying the per-member index weight. It is a payload-carrying junction (an explicit
/// association entity, like JPA's join-table-with-extra-columns), not a pure skip navigation: the
/// composite key (StockIndexId, CompanyId) is the upsert key the importer clears-and-reinserts on.
/// </summary>
public class IndexConstituent
{
    public long StockIndexId { get; set; }
    public long CompanyId { get; set; }

    // Index weight in percent (e.g. 8.91 = 8.91%), sourced from the proxy ETF's holdings.
    // Null when the proxy ETF didn't list this member (membership known, weight unknown).
    public double? WeightPct { get; set; }

    [ForeignKey(nameof(StockIndexId))]
    public virtual StockIndex? StockIndex { get; set; }

    [ForeignKey(nameof(CompanyId))]
    public virtual Company? Company { get; set; }
}
