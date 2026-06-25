using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Provisioning;

/// <summary>
/// The single source of truth for turning a <see cref="CompanyCreateModel"/> into a
/// <see cref="Company"/> entity. Every create path (the New Company form, index-import provisioning,
/// counterparty linking, financials backfill) routes through here so the field list lives in one
/// place — previously each site hand-copied it and they drifted (e.g. the counterparty copy silently
/// dropped <c>Type</c>; an earlier refactor dropped <c>AsOf</c> + industry).
///
/// Two intents, deliberately not shared:
///   • <see cref="ToEntity"/> — birth: a full assign for a brand-new row, sets every field incl. Type.
///   • <see cref="Apply"/>     — merge: coalesces fetched fields onto an existing row, and skips Type
///     on purpose so re-fetching/backfilling never reclassifies a company.
/// </summary>
public static class CompanyMapper
{
    /// <summary>Materialize a new <see cref="Company"/> from a create-model. Country is passed in
    /// because callers resolve it differently (form: model.CountryId; counterparty: owner fallback).</summary>
    public static Company ToEntity(CompanyCreateModel m, long countryId) =>
        new(m.Name, countryId, m.Sector)
        {
            Cik = m.Cik,
            Type = m.Type,
            FmpIndustry = m.FmpIndustry,
            GicsSubIndustry = m.GicsSubIndustry,
            Industry = m.Industry,
            RevenueTotal = m.RevenueTotal,
            GrossMargin = m.GrossMargin,
            MarketCap = m.MarketCap,
            AsOf = m.AsOf,
            Notes = m.Notes,
            // A row born with a sub-industry is already classified; otherwise it's Pending (awaiting the
            // backfill / AI resolve). NoFit is only ever set by a classifier pass that actually ran and missed.
            ClassifyStatus = m.GicsSubIndustry is not null ? ClassifyStatus.Resolved : ClassifyStatus.Pending
        };

    /// <summary>Merge freshly-fetched data onto an existing company: each field overwrites only when the
    /// model actually carries one (<c>?? e.X</c>), so a partial/premium-gated fetch can't null out good
    /// data. Type is intentionally untouched — a backfill must not reclassify the row.</summary>
    public static void Apply(Company e, CompanyCreateModel m)
    {
        e.Cik = m.Cik ?? e.Cik;
        e.Sector = m.Sector ?? e.Sector;   // coalesce: an unknown (null) fetch sector mustn't wipe a good one
        e.FmpIndustry = m.FmpIndustry ?? e.FmpIndustry;
        e.GicsSubIndustry = m.GicsSubIndustry ?? e.GicsSubIndustry;
        e.Industry = m.Industry ?? e.Industry;
        if (e.GicsSubIndustry is not null) e.ClassifyStatus = ClassifyStatus.Resolved;
        e.MarketCap = m.MarketCap ?? e.MarketCap;
        e.RevenueTotal = m.RevenueTotal ?? e.RevenueTotal;
        e.GrossMargin = m.GrossMargin ?? e.GrossMargin;
        e.AsOf = m.AsOf ?? e.AsOf;
        e.Notes = string.IsNullOrWhiteSpace(m.Notes) ? e.Notes : m.Notes;
        if (m.CountryId > 0) e.CountryId = m.CountryId;
    }
}
