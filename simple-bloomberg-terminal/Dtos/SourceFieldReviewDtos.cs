using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record SourceFieldReviewDto(
    long Id,
    long CompanyId,
    RelationKind Relation,
    long? RevenueSourceId,
    long? CostSourceId,
    long? CompanyRiskId,
    ReviewableField Field,
    string Endpoint,
    string ReferencePointer,
    string ReferenceSnapshot,
    string? ReferencedValue,
    long? FilingId,
    int? Mark,
    string? Rationale,
    DateTime? ReviewedAt,
    string? ReviewerModel);

public record SourceFieldReviewRequestDto
{
    [Required] public long? CompanyId { get; init; }
    [Required] public RelationKind? Relation { get; init; }
    public long? RevenueSourceId { get; init; }
    public long? CostSourceId { get; init; }
    public long? CompanyRiskId { get; init; }
    [Required] public ReviewableField? Field { get; init; }
    [Required] public string Endpoint { get; init; } = string.Empty;
    [Required] public string ReferencePointer { get; init; } = string.Empty;
    [Required] public string ReferenceSnapshot { get; init; } = string.Empty;
    public string? ReferencedValue { get; init; }
    public long? FilingId { get; init; }
    public int? Mark { get; init; }
    public string? Rationale { get; init; }
    public DateTime? ReviewedAt { get; init; }
    public string? ReviewerModel { get; init; }
}
