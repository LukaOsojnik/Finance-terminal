using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Picks the single GICS industry within a given sector for a company, via DeepSeek. Shared by the
/// New Company FMP fetch (label miss), the private-company discovery, and the counterparty stub
/// path (ticker-less companies, which otherwise had no industry). Returns null on no fit / failure.
/// </summary>
public interface IIndustryClassifier
{
    Task<GicsIndustry?> ClassifyAsync(Sector sector, string? companyName, string? sourceLabel, CancellationToken ct = default);
}
