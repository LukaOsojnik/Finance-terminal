using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record TradeBlocDto(
    long Id,
    string Name,
    string Code,
    string? Description,
    DateOnly? FoundedDate,
    List<RelatedRefDto> Countries);

public record TradeBlocRequestDto
{
    [Required] public string Name { get; init; } = string.Empty;
    [Required] public string Code { get; init; } = string.Empty;
    public string? Description { get; init; }
    public DateOnly? FoundedDate { get; init; }
    public long[] CountryIds { get; init; } = [];
}
