namespace simple_bloomberg_terminal.Services.Discovery;

// A company profile discovered from the web (Perplexity sonar) for a PRIVATE company that has no
// ticker, so FMP/Yahoo can't supply it. Sector is one of the 11 GICS sector enum names; Industry is
// a free-form label the caller maps through the same FmpMapper lookup used for FMP. RevenueUsd and
// GrossMargin (0â€“1) are best-effort ESTIMATES â€” flagged as such when stored.
public record CompanyProfileResult(
    string? Name,
    string? Sector,
    string? Industry,
    string? CountryCode,
    string? Description,
    double? RevenueUsd,
    double? GrossMargin,
    int? RevenueYear,    // the fiscal/calendar year RevenueUsd refers to (so the row is dated to it, not today)
    double? ValuationUsd,   // latest valuation / market cap (private: post-money from a funding round)
    IReadOnlyList<string> Sources);   // the web pages sonar cited for this profile (top-level `citations`)

/// <summary>
/// Web-search-backed profile discovery for private companies. FMP/Yahoo are ticker-keyed, so a
/// private company (no ticker) can't be fetched there; this asks Perplexity sonar for the company's
/// sector / industry / country / description and an estimated revenue + gross margin. Persists
/// nothing â€” the controller maps the result onto the create form for the user to review. Transport
/// failure throws; an unparseable/empty answer returns null.
/// </summary>
public interface ICompanyProfileDiscovery
{
    Task<CompanyProfileResult?> DiscoverAsync(string companyName, CancellationToken ct = default);
}
