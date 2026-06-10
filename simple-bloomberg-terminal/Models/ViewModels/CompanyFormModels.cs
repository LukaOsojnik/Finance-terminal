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

    // PUBLIC by default; the private (AI-discovery) create path sets PRIVATE. Carried hidden through
    // the discover -> Create POST round-trip like Symbol.
    public CompanyType Type { get; set; } = CompanyType.PUBLIC;

    public GicsIndustry? Industry { get; set; }
    public double? RevenueTotal { get; set; }
    public double? MarketCap { get; set; }

    [Range(-1, 1)]
    public double? GrossMargin { get; set; }

    public DateOnly? AsOf { get; set; }
    public string? Notes { get; set; }

    // Carried hidden from the FMP prefill (Fetch) through to the Create POST so the save step can
    // re-fetch the full financial history by ticker. Null for a purely manual create.
    public string? Symbol { get; set; }
}

public class CompanyEditModel : CompanyCreateModel
{
    public long Id { get; set; }
}
