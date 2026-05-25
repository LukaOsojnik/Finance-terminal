using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record GdpSnapshotDto(
    long Id,
    long CountryId,
    int Year,
    double GdpUsd);

public record GdpSnapshotRequestDto
{
    [Required] public long? CountryId { get; init; }
    [Range(1800, 2200)] public int Year { get; init; }
    public double GdpUsd { get; init; }
}
