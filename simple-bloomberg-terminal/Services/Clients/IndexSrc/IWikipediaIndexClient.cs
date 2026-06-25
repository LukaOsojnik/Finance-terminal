namespace simple_bloomberg_terminal.Services.Clients.IndexSrc;

/// <summary>
/// HTTP-only boundary to Wikipedia for index membership. Fetches an index page and parses its
/// "constituents" table into ticker (+ optional CIK) rows. No persistence, no business logic.
/// </summary>
public interface IWikipediaIndexClient
{
    // pagePath is the wiki path, e.g. "/wiki/Nasdaq-100".
    Task<List<WikiConstituent>> GetConstituentsAsync(string pagePath);
}
