namespace simple_bloomberg_terminal.Services;

// Yahoo Finance quoteSummary?modules=financialData shapes (only what we read). Numbers come
// wrapped as { "raw": 123 }; financialCurrency is a plain string.
public record YahooEnvelope(YahooQuoteSummary? QuoteSummary);
public record YahooQuoteSummary(List<YahooResult>? Result);
public record YahooResult(YahooFinancialData? FinancialData);
public record YahooFinancialData(YahooNum? TotalRevenue, YahooNum? GrossMargins, string? FinancialCurrency);
public record YahooNum(double? Raw);

// Flattened result the service consumes.
public record YahooFinancials(double? Revenue, double? GrossMargins, string? Currency);
