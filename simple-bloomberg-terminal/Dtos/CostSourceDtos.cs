using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record CostSourceDto(
    long Id,
    CostBase CostBase,
    string Name,
    double? Value,
    double? Percentage,
    DataSource? DataSource,
    long CompanyId,
    long? RelatedCompanyId);

public record CostSourceRequestDto
{
    public CostBase CostBase { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public long? CompanyId { get; init; }
    public double? Value { get; init; }
    public double? Percentage { get; init; }
    public DataSource? DataSource { get; init; }
    public long? RelatedCompanyId { get; init; }
}
