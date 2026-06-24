using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Resolves a company's GICS sub-industry (163-tier) — the single judgment step — from its raw vendor
/// (FMP) industry label, falling back to the company name when no label is available. Distinct labels
/// are mapped once via a cheap model and cached, so the same label never costs a second call. The
/// parent GICS Industry/Sector roll up deterministically from the result. Returns null on no fit / failure.
/// Shared by the New Company FMP fetch, private-company discovery, the counterparty stub path, and the
/// index-import enrich/backfill flows.
/// </summary>
public interface IIndustryClassifier
{
    Task<GicsSubIndustry?> ResolveSubIndustryAsync(Sector sector, string? fmpLabel, string? companyName, CancellationToken ct = default);
}
