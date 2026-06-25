using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Services.Classification;

/// <summary>
/// Resolves a company's GICS sub-industry (163-tier) â€” the single judgment step â€” from its raw vendor
/// (FMP) industry label, falling back to the company name when no label is available. Distinct labels
/// are mapped once via a cheap model and cached, so the same label never costs a second call. The
/// parent GICS Industry/Sector roll up deterministically from the result. Returns null on no fit / failure.
/// Shared by the New Company FMP fetch, private-company discovery, the counterparty stub path, and the
/// index-import enrich/backfill flows.
/// </summary>
public interface IIndustryClassifier
{
    /// <param name="sector">The company's (trusted) source sector, or null when unknown. When given, the
    /// model is first constrained to that sector's sub-industries; on a no-fit (or when null) it retries
    /// UNCONSTRAINED across every sub-industry, because vendor sectors disagree with GICS (FMP files solar
    /// under Energy, but GICS has no solar sub-industry there). The chosen sub's Industry/Sector roll up
    /// deterministically â€” so a fallback pick self-heals a wrong/missing source sector at the call site.</param>
    /// <param name="description">Free-text company description (e.g. FMP/sonar blurb). Fed to the
    /// unconstrained fallback so it can reason from what the company actually does, not just the label.</param>
    /// <param name="bypassCache">Skip the label cache for both READ and WRITE â€” always re-reason and don't
    /// persist the result. Used by the on-demand "Resolve with AI" so the button actually overrides a stale
    /// or ambiguous cached mapping instead of echoing it back.</param>
    Task<GicsSubIndustry?> ResolveSubIndustryAsync(Sector? sector, string? fmpLabel, string? companyName,
        string? description = null, bool bypassCache = false, CancellationToken ct = default);
}
