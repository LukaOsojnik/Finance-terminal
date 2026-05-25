using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record CountryDetailsDto(
    long CountryId,
    string MarketPosition);

public record CountryDetailsRequestDto
{
    [Required] public long? CountryId { get; init; }
    [Required] public string MarketPosition { get; init; } = string.Empty;
}
