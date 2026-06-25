using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Indices;

/// <summary>
/// Imports a market index's membership and links members to existing companies (by CIK, resolving
/// ticker -> CIK via the SEC map). Two sources, picked per index:
/// <list type="bullet">
/// <item><b>SPDR</b> â€” when the index has a State Street SPDR ETF (SPY, DIA, XLKâ€¦): the daily-holdings
/// file gives REAL published weights and a per-holding GICS sector, so weights are accurate and the
/// index's sector is inferred for free.</item>
/// <item><b>Wikipedia</b> â€” the fallback for everything else (FTSE, DAX, non-US): membership is
/// scraped and the index is cap-weighted from each matched company's stored MarketCap.</item>
/// </list>
/// Members that don't resolve to an existing Company are skipped; the result reports coverage.
/// </summary>
public interface IIndexImportService
{
    // Import one index. EtfTicker present => try SPDR first (real weights), falling back to WikiPage if
    // the ticker isn't a SPDR fund. EtfTicker absent => Wikipedia + cap-weight. `progress` surfaces the
    // current phase to the async-import job widget.
    Task<IndexImportResult> ImportAsync(IndexImportRequest request, IProgress<string>? progress = null);
}

// One import request. Code/Name identify the StockIndex (Code is the upsert key). WikiPage is the
// English-Wikipedia "/wiki/..." constituents page (Wikipedia source). EtfTicker is the SPDR ETF symbol
// (SPDR source). Sector/Region seed the catalog grouping; for SPDR the sector is overridden by what the
// holdings file actually contains.
public record IndexImportRequest(
    string Code, string Name, string? WikiPage, string? EtfTicker, Sector? Sector, string? Region);

public record IndexImportResult(
    long IndexId,
    string IndexName,
    int TotalConstituents,   // members the source listed
    int Matched,             // members linked to a Company (existing + newly provisioned)
    int Provisioned,         // members that didn't exist and were created from FMP during this import
    IReadOnlyList<long> ProvisionedIds,  // ids of the newly-created companies, for background enrichment
    double WeightCovered,    // summed WeightPct of the matched members
    string Source,           // "SPDR" or "Wikipedia" â€” which source actually supplied the data
    bool CanContinue);       // true => auto-provisioning stopped early (no FMP key / daily cap), so a
                             // continue under another user's key would link more members
