using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Web-search-backed counterparty discovery. SEC filings rarely name the supplier/customer companies
/// behind a cost or revenue line, so this asks a search-native model (Perplexity sonar) for the named
/// suppliers and customers of a company and returns them as suggestions for the user to confirm.
/// Persists nothing; the confirm step (link-counterparty) creates the rows. Transport failure throws.
///
/// Runs Perplexity-style: a planner first decomposes the company+segments into several focused
/// sub-queries, then each sub-query is its own grounded web search. Results stream as a sequence of
/// <see cref="DiscoveryEvent"/> (plan, then searching/result per query) so the page shows a live feed.
/// </summary>
public interface ICounterpartyDiscovery
{
    /// <param name="side">CUSTOMER to find buyers for revenue segments, SUPPLIER to find sellers for cost segments.</param>
    /// <param name="segments">The company's segment names (revenue or cost); empty => the model identifies them itself.</param>
    /// <param name="valued">false => find named counterparties per segment; true => find the BIGGEST counterparties and the estimated USD value of each contract.</param>
    IAsyncEnumerable<DiscoveryEvent> DiscoverAsync(
        long companyId, string side, IReadOnlyList<string> segments, bool valued = false, CancellationToken ct = default);
}
