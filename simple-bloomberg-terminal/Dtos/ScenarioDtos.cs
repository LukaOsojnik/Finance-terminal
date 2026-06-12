using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record ScenarioDto(
    long Id,
    string Name,
    string? Description,
    List<ScenarioShockDto> Shocks);

public record ScenarioRequestDto
{
    [Required] public string Name { get; init; } = string.Empty;
    public string? Description { get; init; }
}
