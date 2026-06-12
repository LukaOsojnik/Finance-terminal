using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record CompanyRiskDto(
    long Id,
    RiskScope Scope,
    string Name,
    string? Note,
    DataSource? DataSource,
    long CompanyId);

public record CompanyRiskRequestDto
{
    [Required] public RiskScope? Scope { get; init; }
    [Required] public string Name { get; init; } = string.Empty;
    public string? Note { get; init; }
    public DataSource? DataSource { get; init; }
    [Required] public long? CompanyId { get; init; }
}
