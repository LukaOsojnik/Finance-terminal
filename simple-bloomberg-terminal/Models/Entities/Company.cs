using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class Company
{
    public Company(string name, long countryId, Sector sector)
    {
        Name = name;
        CountryId = countryId;
        Sector = sector;
    }

    [Key]
    public long Id { get; set; }
    public string Name { get; set; }
    public string? Cik { get; set; }
    public CompanyType Type { get; set; } = CompanyType.PUBLIC;
    public long CountryId { get; set; }
    public Sector Sector { get; set; }

    // The raw vendor (FMP) industry label, stored verbatim — the finest, source-of-truth tier. Kept
    // so industry can be re-resolved later without re-fetching FMP, and as the LLM's strongest signal.
    public string? FmpIndustry { get; set; }

    // GICS sub-industry (163-tier), reasoned by the LLM from FmpIndustry. Industry is its rollup.
    public GicsSubIndustry? GicsSubIndustry { get; set; }

    // GICS Industry (74-tier) — a denormalized cache of GicsSubIndustry.GetIndustry() for cheap querying.
    public GicsIndustry? Industry { get; set; }
    public double? RevenueTotal { get; set; }
    public double? GrossMargin { get; set; }
    public double? MarketCap { get; set; }
    public DateOnly? AsOf { get; set; }
    public string? Notes { get; set; }
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CountryId")]
    public virtual Country? Country { get; set; }

    [InverseProperty("Company")]
    public virtual ICollection<RevenueSource> RevenueSources { get; set; } = [];

    [InverseProperty("Company")]
    public virtual ICollection<CostSource> CostSources { get; set; } = [];

    [InverseProperty("Company")]
    public virtual ICollection<CompanyRisk> CompanyRisks { get; set; } = [];

    [InverseProperty("Company")]
    public virtual ICollection<CompanyFinancial> Financials { get; set; } = [];

    [InverseProperty("RelatedCompany")]
    public virtual ICollection<RevenueSource> RevenueFromDependents { get; set; } = [];

    [InverseProperty("RelatedCompany")]
    public virtual ICollection<CostSource> CostFromDependents { get; set; } = [];

    public virtual ICollection<Event> Events { get; set; } = [];
}
