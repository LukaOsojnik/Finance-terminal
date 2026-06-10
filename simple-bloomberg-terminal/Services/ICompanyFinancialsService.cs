using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Builds a company's dated financial history (<see cref="CompanyFinancial"/> rows) from FMP's
/// income / ratios / balance-sheet / cash-flow statements, merged by (FiscalYear, Period) across
/// both annual and quarterly granularities. When FMP is premium-gated (non-US -> 402), falls back
/// to Yahoo's annual income history (revenue + net income only). Builds in memory; persistence is
/// the caller's job (via the repository upsert). Returns an empty list if nothing is available.
/// </summary>
public interface ICompanyFinancialsService
{
    Task<IReadOnlyList<CompanyFinancial>> BuildAsync(long companyId, string symbol);
}
