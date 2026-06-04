namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP-only boundary to Financial Modeling Prep. No business logic, no persistence — just
/// fetch and deserialize. A 404 (or empty array) surfaces as <c>null</c>; any other transport
/// failure throws. Registered as a typed <c>HttpClient</c>.
/// </summary>
public interface IFmpApiClient
{
    Task<FmpProfile?> GetProfileAsync(string symbol);
    Task<FmpIncome?> GetLatestIncomeAsync(string symbol);
}
