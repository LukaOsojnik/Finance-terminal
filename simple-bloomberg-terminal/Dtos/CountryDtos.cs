using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record CountryDto(
    long Id,
    string Code,
    string Name,
    string Region,
    string CurrencyCode,
    double? GdpUsd,
    long? Population,
    double? RiskRating,
    string? Notes);

public record CountryRequestDto
{
    [Required] public string Code { get; init; } = string.Empty;
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public string Region { get; init; } = string.Empty;
    [Required] public string CurrencyCode { get; init; } = string.Empty;
    public double? GdpUsd { get; init; }
    public long? Population { get; init; }
    public double? RiskRating { get; init; }
    public string? Notes { get; init; }
}
