using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Services.Financials;

public interface IStockService
{
    /// <summary>
    /// Fetch EDGAR facts for <paramref name="company"/> (which must have a Cik), replace its
    /// prior EDGAR-tagged source rows with the mapped revenue/cost rows, and return the refreshed
    /// company graph. Does not create events or filings. Throws <see cref="EdgarException"/> on
    /// SEC failure.
    /// </summary>
    Task<CompanyDto> RefreshAsync(Company company);
}
