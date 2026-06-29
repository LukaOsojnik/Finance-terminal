namespace McpServer;

// Output shapes returned by the tools. Every result carries an explicit `status` + human `note` so a
// weak model can tell "this data isn't in the database" apart from "this company has none of it" — it
// must never infer a fact from a silently-empty field. Status values:
//   "present"      — data found and returned
//   "missing"      — the company is tracked but this category has no data loaded yet
//   "unavailable"  — the category can't exist for this company (e.g. filings for a non-SEC filer)
//   "no_match"     — (find_company) no tracked company matched the query
//   "not_found"    — the given companyId is not a tracked company
//   "error"        — an upstream source (e.g. SEC) could not be reached

public record CompanyMatch(long CompanyId, string Name, string? Cik, string? Sector, string? Country);

public record FindCompanyResult(string Status, string Note, List<CompanyMatch> Matches);

public record Classification(string? Sector, string? Industry, string? SubIndustry);

public record StockProfile(
    long CompanyId,
    string Name,
    string? Cik,
    Classification Classification,
    double? RevenueTotal,
    double? GrossMargin,
    string? AsOf,
    string? Country,
    string? CountryCode,
    string? Notes,
    List<string> MissingFields);

public record ProfileResult(string Status, string Note, StockProfile? Profile);

public record FinancialPeriod(
    int FiscalYear,
    string Period,
    string? EndDate,
    string? ReportedCurrency,
    string Source,
    double? Revenue,
    double? GrossProfit,
    double? OperatingIncome,
    double? Ebitda,
    double? NetIncome,
    double? Eps,
    double? GrossMargin,
    double? NetMargin,
    double? FreeCashFlow);

public record FinancialsResult(string Status, string Note, bool Stale, string? NewestCapturedAt, List<FinancialPeriod> Periods);

public record RiskItem(string Scope, string Name, string? Note);

public record RisksResult(string Status, string Note, int Count, List<RiskItem> Risks);

public record Counterparty(string Classification, string Name, double? Value, double? Percentage, long? RelatedCompanyId);

public record RelationshipsResult(string Status, string Note, List<Counterparty> Revenue, List<Counterparty> Costs);

public record VolumePoint(string WeekStart, long Volume);

// `series` is capped to the most recent weeks (TotalWeeks reports the true count) so the payload stays
// small for weak models; Stale flags a series whose latest bar is well in the past.
public record VolumeResult(string Status, string Note, bool Stale, string? LatestWeek, int TotalWeeks, List<VolumePoint> Series);

public record FilingItem(string? Form, string? FilingDate, string? AccessionNumber, string? DocumentUrl);

public record FilingsResult(string Status, string Note, int Count, List<FilingItem> Filings);
