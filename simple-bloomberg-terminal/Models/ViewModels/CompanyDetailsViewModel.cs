using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

// Java: record CompanyDetailsViewModel(Company company, List<Event> relatedEvents, String sectorLabel, String industryLabel)
// C#: class with required properties and init-only setters via object initializer
public class CompanyDetailsViewModel
{
    public required Company Company { get; set; }

    public IEnumerable<Event> RelatedEvents { get; set; } = [];

    // Active revenue/cost sources for this company (with RelatedCompany + Filing loaded), so the
    // profile can list them and delete each (cascading to its filing cluster).
    public IEnumerable<RevenueSource> RevenueSources { get; set; } = [];
    public IEnumerable<CostSource> CostSources { get; set; } = [];
    public IEnumerable<CompanyRisk> CompanyRisks { get; set; } = [];

    // Dated financial history (one per fiscal period), newest first, for the history table.
    public IEnumerable<CompanyFinancial> Financials { get; set; } = [];

    // Weekly trading-volume history, oldest first, for the volume graph next to Financial Overview.
    public IReadOnlyList<CompanyVolumeHistory> VolumeHistory { get; set; } = [];

    // Sector enum formatted for display (underscores → spaces)
    public required string SectorLabel { get; set; }

    // Industry enum formatted, or "—" if null
    public required string IndustryLabel { get; set; }

    // GICS sub-industry (finest tier) formatted, or "—" if unresolved
    public required string SubIndustryLabel { get; set; }
}
