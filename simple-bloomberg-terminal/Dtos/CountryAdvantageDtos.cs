using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record CountryAdvantageDto(
    long Id,
    long CountryId,
    string Text);

public record CountryAdvantageRequestDto
{
    [Required] public long? CountryId { get; init; }
    [Required] public string Text { get; init; } = string.Empty;
}
