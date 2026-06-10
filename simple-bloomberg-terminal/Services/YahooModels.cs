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

// quoteSummary?modules=incomeStatementHistory — annual statements. Yahoo has gutted the line items
// (costOfRevenue/operatingIncome/etc come back as 0 or {}), so only endDate, totalRevenue and
// netIncome are reliable. Used as the non-US per-year fallback when FMP is premium-gated.
public record YahooIncomeEnvelope(YahooIncomeQuoteSummary? QuoteSummary);
public record YahooIncomeQuoteSummary(List<YahooIncomeResult>? Result);
public record YahooIncomeResult(YahooIncomeHistory? IncomeStatementHistory);
public record YahooIncomeHistory(List<YahooIncomeStatement>? IncomeStatementHistory);
public record YahooIncomeStatement(YahooDate? EndDate, YahooNum? TotalRevenue, YahooNum? NetIncome);
public record YahooDate(long? Raw, string? Fmt);

// Flattened annual income row the service consumes (one per fiscal year).
public record YahooAnnualIncome(DateOnly? EndDate, double? Revenue, double? NetIncome);
