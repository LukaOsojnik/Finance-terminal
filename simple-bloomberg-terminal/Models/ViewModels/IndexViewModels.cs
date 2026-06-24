using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

// One row of an index's sector (or industry) breakdown: how many member companies fall in the
// group and their summed index weight. Weight is derived from IndexConstituent.WeightPct; the
// grouping key is the live Company.Sector / Company.Industry (not a stored copy).
public record IndexBreakdownSlice(string Label, int CompanyCount, double WeightPct);

// The Indices catalog page: the tracked indices plus the recent import jobs (so a partial import another
// user started — its FMP provisioning capped — is visible and continuable by anyone).
public record IndicesPageViewModel(
    IReadOnlyList<StockIndex> Indices,
    IReadOnlyList<IndexImportJob> Jobs);

public class IndexDetailViewModel
{
    public required StockIndex Index { get; init; }
    public required IReadOnlyList<IndexBreakdownSlice> BySector { get; init; }
    public required IReadOnlyList<IndexBreakdownSlice> ByIndustry { get; init; }

    // Members loaded vs. members with a known weight — surfaces how much of the index the breakdown covers.
    public int MemberCount { get; init; }
    public double WeightCovered { get; init; }
}
