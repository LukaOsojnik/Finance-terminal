using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record ScenarioShockDto(
    long Id,
    long ScenarioId,
    ImpactKind Kind,
    ShockTarget Target,
    Sector? Sector,
    CostFactor? Factor,
    double Magnitude);

public record ScenarioShockRequestDto
{
    [Required] public long? ScenarioId { get; init; }
    [Required] public ImpactKind? Kind { get; init; }
    [Required] public ShockTarget? Target { get; init; }
    public Sector? Sector { get; init; }
    public CostFactor? Factor { get; init; }
    public double Magnitude { get; init; }
}
