namespace simple_bloomberg_terminal.Models.Enums;

// The reporting period a CompanyFinancial row covers. FY = full fiscal year (annual statement);
// Q1–Q4 = the corresponding fiscal quarter. FMP reports this directly in its "period" field
// ("FY"/"Q1".."Q4"); the Yahoo non-US fallback only produces annual rows, so FY.
public enum FiscalPeriod
{
    FY,
    Q1,
    Q2,
    Q3,
    Q4
}
