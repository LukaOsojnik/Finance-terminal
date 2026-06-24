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
    string? Description,
    double? MarketCap);

// The four statement endpoints below all carry the same period key (fiscalYear + period, where
// period is "FY" for annual or "Q1".."Q4" for quarterly), so the assembler can merge them into one
// CompanyFinancial row per (FiscalYear, Period). Each is a JSON array; we request period + limit.

// /stable/income-statement?symbol={ticker}&period={annual|quarter}&limit={n}
// The stable endpoint returns raw line items only (no *Ratio fields), so gross margin is
// computed from grossProfit / revenue in the mapper.
public record FmpIncome(
    string? Date,
    string? FiscalYear,
    string? Period,
    string? ReportedCurrency,
    double? Revenue,
    double? CostOfRevenue,
    double? GrossProfit,
    double? OperatingIncome,
    double? Ebitda,
    double? NetIncome,
    double? Eps);

// /stable/ratios?symbol={ticker}&period={annual|quarter}&limit={n} — ready-made margins/ratios.
public record FmpRatio(
    string? FiscalYear,
    string? Period,
    double? GrossProfitMargin,
    double? OperatingProfitMargin,
    double? NetProfitMargin,
    double? CurrentRatio,
    double? DebtToEquityRatio);

// /stable/balance-sheet-statement?symbol={ticker}&period={annual|quarter}&limit={n}
public record FmpBalance(
    string? FiscalYear,
    string? Period,
    double? CashAndShortTermInvestments,
    double? TotalDebt);

// /stable/cash-flow-statement?symbol={ticker}&period={annual|quarter}&limit={n}
public record FmpCashFlow(
    string? FiscalYear,
    string? Period,
    double? OperatingCashFlow,
    double? FreeCashFlow);

