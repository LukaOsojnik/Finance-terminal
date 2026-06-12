using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Dtos;

public record FilingDto(
    long Id,
    long CompanyId,
    string AccessionNumber,
    string? Form,
    DateTime? FilingDate,
    string? PrimaryDocUrl);

public record FilingRequestDto
{
    [Required] public long? CompanyId { get; init; }
    [Required] public string AccessionNumber { get; init; } = string.Empty;
    public string? Form { get; init; }
    public DateTime? FilingDate { get; init; }
    public string? PrimaryDocUrl { get; init; }
}
