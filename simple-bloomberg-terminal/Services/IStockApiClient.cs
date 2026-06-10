namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP-only boundary to SEC EDGAR. No business logic, no persistence — just fetch and
/// deserialize. A 404 from SEC surfaces as <c>null</c>; any other transport failure throws
/// (the service maps that to 503). Registered as a typed <c>HttpClient</c>.
/// </summary>
public interface IStockApiClient
{
    Task<EdgarCompanyFacts?> GetCompanyFacts(string cik10);
    Task<EdgarSubmissions?> GetSubmissions(string cik10);
    Task<string?> ResolveCik(string ticker);

    // Reverse of ResolveCik: the SEC ticker map keyed by 10-digit zero-padded CIK -> primary ticker
    // (the form Company.Cik is stored in). Loads the whole map once; used to backfill financials for
    // existing companies by their CIK. First ticker wins when a CIK has several share classes.
    Task<IReadOnlyDictionary<string, string>> GetCikTickerMap();

    // Raw passthroughs for the extraction browser (right pane). Return the literal SEC payload
    // (no mapping) so the user can select proof text. 404 -> null.
    Task<string?> GetCompanyFactsJson(string cik10);
    Task<string?> GetFilingDocument(string cik, string accessionNoDashes, string primaryDocument);
}
