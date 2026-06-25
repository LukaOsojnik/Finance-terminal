namespace simple_bloomberg_terminal.Services.Clients.Fmp;

/// <summary>
/// HTTP-only boundary to Financial Modeling Prep. No business logic, no persistence — just
/// fetch and deserialize. A 404 (or empty array) surfaces as <c>null</c>; any other transport
/// failure throws. Registered as a typed <c>HttpClient</c>.
/// </summary>
public interface IFmpApiClient
{
    Task<FmpProfile?> GetProfileAsync(string symbol);
    Task<FmpIncome?> GetLatestIncomeAsync(string symbol);

    // Multi-period statements for building CompanyFinancial history. period is "annual" or "quarter".
    // A 404 / empty array surfaces as null; premium-gated symbols (non-US) throw 402 like the rest.
    Task<List<FmpIncome>?> GetIncomeStatementsAsync(string symbol, string period, int limit);
    Task<List<FmpRatio>?> GetRatiosAsync(string symbol, string period, int limit);
    Task<List<FmpBalance>?> GetBalanceSheetsAsync(string symbol, string period, int limit);
    Task<List<FmpCashFlow>?> GetCashFlowsAsync(string symbol, string period, int limit);
}
