using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CompanyCreateModel
{
    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    [StringLength(32)]
    public string? Cik { get; set; }

    [Required]
    public long CountryId { get; set; }

    [Required]
    public Sector Sector { get; set; }

    public GicsIndustry? Industry { get; set; }
    public double? RevenueTotal { get; set; }

    [Range(-1, 1)]
    public double? GrossMargin { get; set; }

    public DateOnly? AsOf { get; set; }
    public string? Notes { get; set; }
}

public class CompanyEditModel : CompanyCreateModel
{
    public long Id { get; set; }
}
