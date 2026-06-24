using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A named market index (NASDAQ-100, S&amp;P 500, Dow 30) as a membership set over existing
/// <see cref="Company"/> rows. The index itself stores no sector data — a sector/industry
/// breakdown is derived at query time from each member's <see cref="Company.Sector"/>, weighted
/// by the per-member <see cref="IndexConstituent.WeightPct"/>. <c>EtfProxy</c> is the ticker of the
/// ETF whose holdings supply those weights (QQQ for NASDAQ-100, SPY for S&amp;P 500), since the FMP
/// constituent endpoint carries membership but not weight.
/// </summary>
public class StockIndex
{
    public StockIndex(string name, string code)
    {
        Name = name;
        Code = code;
    }

    [Key]
    public long Id { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }          // FMP constituent key, e.g. "nasdaq", "sp500", "dowjones"
    public string? EtfProxy { get; set; }     // ticker of the weight-source ETF, e.g. "QQQ"
    public string? Provider { get; set; }     // where membership came from, e.g. "Wikipedia", "SPDR"
    public string? Description { get; set; }

    // Sector classification used to GROUP indices on the catalog page. Null = a broad-market index
    // (S&P 500, FTSE 100) that spans every sector; a value means a sector-specific index (XLK ->
    // INFORMATION_TECHNOLOGY). Set by Perplexity at discovery or inferred from a SPDR sector ETF.
    public Sector? Sector { get; set; }
    public string? Region { get; set; }       // short label for display/grouping, e.g. "US", "UK", "Global"

    // Snapshot of Σ(constituent MarketCap) stamped at import time — the "size" the catalog sorts by.
    // Point-in-time like the cap-weights, so it lives next to AsOf rather than being recomputed live.
    public double? TotalMarketCap { get; set; }

    public DateOnly? AsOf { get; set; }       // when membership + weights were last imported
    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<IndexConstituent> Constituents { get; set; } = [];
}
