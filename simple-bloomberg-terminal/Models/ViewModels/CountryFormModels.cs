using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryCreateModel
{
    [Required, StringLength(8)]
    public string Code { get; set; } = string.Empty;

    [Required, StringLength(120)]
    public string Name { get; set; } = string.Empty;

    [Required, StringLength(80)]
    public string Region { get; set; } = string.Empty;

    [Required, StringLength(8)]
    public string CurrencyCode { get; set; } = string.Empty;

    public double? GdpUsd { get; set; }
    public long? Population { get; set; }

    [Range(0, 100)]
    public double? RiskRating { get; set; }

    public string? Notes { get; set; }
}

public class CountryEditModel : CountryCreateModel
{
    public long Id { get; set; }
}
