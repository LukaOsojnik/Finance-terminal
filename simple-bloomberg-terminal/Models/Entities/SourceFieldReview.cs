using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class SourceFieldReview
{
    [Key]
    public long Id { get; set; }

    public long CompanyId { get; set; }
    public RelationKind Relation { get; set; }
    public long? RevenueSourceId { get; set; }
    public long? CostSourceId { get; set; }
    public long? CompanyRiskId { get; set; }
    public ReviewableField Field { get; set; }

    public string Endpoint { get; set; } = string.Empty;
    public string ReferencePointer { get; set; } = string.Empty;
    public string ReferenceSnapshot { get; set; } = string.Empty;
    public string? ReferencedValue { get; set; }

    // The filing this per-field proof was drawn from (null when the proof came from Company Facts,
    // not a filing document). Because proof is per-field, one source can cite several filings —
    // one per cell — via its reviews.
    public long? FilingId { get; set; }

    public int? Mark { get; set; }
    public string? Rationale { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewerModel { get; set; }
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    [ForeignKey("RevenueSourceId")]
    public virtual RevenueSource? RevenueSource { get; set; }

    [ForeignKey("CostSourceId")]
    public virtual CostSource? CostSource { get; set; }

    [ForeignKey("CompanyRiskId")]
    public virtual CompanyRisk? CompanyRisk { get; set; }

    [ForeignKey("FilingId")]
    public virtual Filing? Filing { get; set; }
}
