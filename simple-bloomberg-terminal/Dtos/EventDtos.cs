using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record EventDto(
    long Id,
    string Title,
    EventType Type,
    DateTime Date,
    DateTime? EndDate,
    string? Description,
    double? ImpactScore,
    List<RelatedRefDto> Countries,
    List<RelatedRefDto> Companies,
    List<RelatedRefDto> TradeBlocs);

public record EventRequestDto
{
    [Required] public string Title { get; init; } = string.Empty;
    public EventType Type { get; init; }
    public DateTime Date { get; init; }
    public DateTime? EndDate { get; init; }
    public string? Description { get; init; }
    public double? ImpactScore { get; init; }
    public long[] CountryIds { get; init; } = [];
    public long[] CompanyIds { get; init; } = [];
    public long[] TradeBlocIds { get; init; } = [];
}
