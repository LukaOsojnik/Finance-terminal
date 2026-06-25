using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// One company's weekly trading volume for a single week, backfilled from Yahoo Finance's chart
/// endpoint (<c>/v8/finance/chart?interval=1wk&amp;range=max</c>). Unlike <see cref="CompanyFinancial"/>
/// (fiscal-period fundamentals) this is the price-feed time series powering the multi-year volume
/// graph. One row per (CompanyId, WeekStart) — re-fetching upserts in place instead of duplicating.
/// Weekly granularity keeps the table ~20× smaller than daily while matching the chart's resolution.
/// </summary>
public class CompanyVolumeHistory
{
    public CompanyVolumeHistory(long companyId, DateOnly weekStart, long volume)
    {
        CompanyId = companyId;
        WeekStart = weekStart;
        Volume = volume;
    }

    [Key]
    public long Id { get; set; }

    public long CompanyId { get; set; }

    // Monday of the week Yahoo buckets the bar under (its timestamp is the week's first trading day).
    public DateOnly WeekStart { get; set; }

    // Shares traded during the week. Large-cap weeks exceed int.MaxValue, so this is long.
    public long Volume { get; set; }

    public DateTime CapturedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }
}
