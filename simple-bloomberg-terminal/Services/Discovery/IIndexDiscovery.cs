using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Discovery;

// One stock-market index suggested by Perplexity for a user query. WikiPage is the English-Wikipedia
// "/wiki/..." path of the page carrying the index's constituents/components table — the exact input
// the Wikipedia import pipeline needs. Code is a short slug used as the StockIndex upsert key.
// Sector groups the index on the catalog page (null = broad-market). EtfTicker is the SPDR ETF that
// tracks this index when one exists (SPY, XLK, ...) — its presence lets import pull REAL weights from
// the SPDR holdings file instead of cap-weighting; null means Wikipedia+cap-weight is the only source.
public record DiscoveredIndex(
    string Code, string Name, string WikiPage, string? Region, Sector? Sector, string? EtfTicker);

/// <summary>
/// Web-search index discovery: turns a free-text query ("European tech indices") into a list of
/// indices the existing Wikipedia→SEC→DB import pipeline can fetch. One grounded Perplexity sonar
/// call; persists nothing. The caller renders the suggestions for the user to pick which to import.
/// </summary>
public interface IIndexDiscovery
{
    Task<IReadOnlyList<DiscoveredIndex>> DiscoverAsync(string query, CancellationToken ct = default);
}
