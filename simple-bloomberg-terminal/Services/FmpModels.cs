namespace simple_bloomberg_terminal.Services;

// Minimal shapes for the FMP /stable JSON we actually read. System.Net.Http.Json uses
// JsonSerializerDefaults.Web (camelCase, case-insensitive), so the camelCase FMP keys bind
// to these PascalCase properties without attributes. Both endpoints return a JSON array.

// /stable/profile?symbol={ticker}
public record FmpProfile(
    string? Symbol,
    string? CompanyName,
    string? Cik,
    string? Currency,
    string? Country,
    string? Sector,
    string? Industry,
    string? Description);

// /stable/income-statement?symbol={ticker}&limit=1
// The stable endpoint returns raw line items only (no *Ratio fields), so gross margin is
// computed from grossProfit / revenue in the mapper.
public record FmpIncome(
    string? Date,
    string? ReportedCurrency,
    double? Revenue,
    double? GrossProfit);
