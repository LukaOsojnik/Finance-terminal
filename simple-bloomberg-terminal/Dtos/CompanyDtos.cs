using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record CompanyDto(
    long Id,
    string Name,
    string? Cik,
    Sector Sector,
    GicsIndustry? Industry,
    double? RevenueTotal,
    double? GrossMargin,
    DateOnly? AsOf,
    string? Notes,
    CountryDto? Country,
    List<RevenueSourceDto> RevenueSources,
    List<CostSourceDto> CostSources);

public record CompanyRequestDto
{
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public long? CountryId { get; init; }
    public Sector Sector { get; init; }
    public string? Cik { get; init; }
    public GicsIndustry? Industry { get; init; }
    public double? RevenueTotal { get; init; }
    public double? GrossMargin { get; init; }
    public DateOnly? AsOf { get; init; }
    public string? Notes { get; init; }
}
