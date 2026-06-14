using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A risk a company discloses (extracted from Item 1A risk factors / Item 7A market risk). Unlike
/// revenue/cost rows it has no money figures — just a short Name, a <see cref="RiskScope"/> bucket,
/// and a free-text Note. Proof per cell lives in <see cref="SourceFieldReview"/> (Relation = RISK).
/// </summary>
public class CompanyRisk
{
    public CompanyRisk(RiskScope scope, string name, long companyId)
    {
        Scope = scope;
        Name = name;
        CompanyId = companyId;
    }

    [Key]
    public long Id { get; set; }
    public RiskScope Scope { get; set; }
    public string Name { get; set; }
    public string? Note { get; set; }
    public DataSource? DataSource { get; set; }
    public long CompanyId { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Contribution review (Status defaults to Approved=0): user-contributed rows are Pending until a
    // Manager rules on them; ContributedBy is who proposed it; Supersedes points at the live Approved
    // row this pending edit would replace (null = a brand-new addition). See ContributionStatus.
    public ContributionStatus Status { get; set; }
    public string? ContributedByUserId { get; set; }
    public long? SupersedesId { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    [ForeignKey("ContributedByUserId")]
    public virtual AppUser? ContributedBy { get; set; }

    // Per-field proof rows; their distinct filings are the risk's proof filings.
    public virtual ICollection<SourceFieldReview> Reviews { get; set; } = [];
}
