namespace McpServer;

// Small, stable enums mirrored from the terminal (Models/Enums/*) with EXPLICIT ordinals. The terminal
// serializes enums as their integer ordinal (no JsonStringEnumConverter), so we redeclare these to turn
// those numbers back into readable names; "= N" guards against silent drift if the source is reordered.
// GICS classification (Sector/Industry/SubIndustry — the big 11/74/163 enums) is intentionally NOT
// mirrored here: the terminal now projects those as ready-made name strings on CompanyDto.
public enum RiskScope
{
    MACROECONOMIC = 0, INDUSTRY = 1, BUSINESS = 2, LEGAL_REGULATORY = 3, FINANCIAL = 4, GENERAL = 5
}

public enum SourceType { CUSTOMER = 0, SEGMENT = 1, REGION = 2, PRODUCT = 3 }

public enum CostBase { COGS = 0, OPEX = 1, TOTAL_COSTS = 2 }

public enum FiscalPeriod { FY = 0, Q1 = 1, Q2 = 2, Q3 = 3, Q4 = 4 }

public enum DataSource { EDGAR = 0, MANUAL = 1, CLAUDE_ESTIMATED = 2, OPENBB = 3, FMP = 4, YAHOO = 5 }

// ---- Terminal API response shapes (subset of the terminal's DTOs — only the fields we surface) ----
// Property names are matched case-insensitively against the terminal's camelCase JSON.

public record TCountry(string? Code, string? Name, string? Region);

public record TRevenueSource(SourceType SourceType, string Name, double? Value, double? Percentage, long? RelatedCompanyId);

public record TCostSource(CostBase CostBase, string Name, double? Value, double? Percentage, long? RelatedCompanyId);

public record TCompany(
    long Id,
    string Name,
    string? Cik,
    string? SectorName,
    string? IndustryName,
    string? SubIndustry,
    double? RevenueTotal,
    double? GrossMargin,
    DateOnly? AsOf,
    string? Notes,
    TCountry? Country,
    List<TRevenueSource>? RevenueSources,
    List<TCostSource>? CostSources);

public record TFinancial(
    long CompanyId,
    int FiscalYear,
    FiscalPeriod Period,
    DateOnly? EndDate,
    string? ReportedCurrency,
    DataSource Source,
    DateTime CapturedAt,
    double? Revenue,
    double? GrossProfit,
    double? OperatingIncome,
    double? Ebitda,
    double? NetIncome,
    double? Eps,
    double? GrossMargin,
    double? NetMargin,
    double? FreeCashFlow);

public record TRisk(long CompanyId, RiskScope Scope, string Name, string? Note);

// One element from GET /api/stock/filings/{id} (live SEC list — an anonymous object).
public record TFiling(string? Form, string? FilingDate, string? AccessionNumber, string? DocumentUrl);

public record TVolumePoint(string WeekStart, long Volume);
