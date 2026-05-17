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
    public long CountryId { get; set; }
    public Sector Sector { get; set; }
    public GicsIndustry? Industry { get; set; }
    public double? RevenueTotal { get; set; }
    public double? GrossMargin { get; set; }
    public DateOnly? AsOf { get; set; }
    public string? Notes { get; set; }
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CountryId")]
    public virtual Country? Country { get; set; }

    [InverseProperty("Company")]
    public virtual ICollection<RevenueSource> RevenueSources { get; set; } = [];

    [InverseProperty("Company")]
    public virtual ICollection<CostSource> CostSources { get; set; } = [];

    [InverseProperty("RelatedCompany")]
    public virtual ICollection<RevenueSource> RevenueFromDependents { get; set; } = [];

    [InverseProperty("RelatedCompany")]
    public virtual ICollection<CostSource> CostFromDependents { get; set; } = [];

    public virtual ICollection<Event> Events { get; set; } = [];
}
