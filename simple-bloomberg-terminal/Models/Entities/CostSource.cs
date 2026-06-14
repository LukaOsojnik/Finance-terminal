using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class CostSource
{
    public CostSource(CostBase costBase, string name, long companyId)
    {
        CostBase = costBase;
        Name = name;
        CompanyId = companyId;
    }

    [Key]
    public long Id { get; set; }
    public CostBase CostBase { get; set; }
    public string Name { get; set; }
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public DataSource? DataSource { get; set; }
    public long CompanyId { get; set; }
    public long? RelatedCompanyId { get; set; }
    public DateTime? DeletedAt { get; set; }

    // Contribution review (Status defaults to Approved=0): user-contributed rows are Pending until a
    // Manager rules on them; ContributedBy is who proposed it; Supersedes points at the live Approved
    // row this pending edit would replace (null = a brand-new addition). See ContributionStatus.
    public ContributionStatus Status { get; set; }
    public string? ContributedByUserId { get; set; }
    public long? SupersedesId { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    [ForeignKey("RelatedCompanyId")]
    public virtual Company? RelatedCompany { get; set; }

    [ForeignKey("ContributedByUserId")]
    public virtual AppUser? ContributedBy { get; set; }

    // Per-field proof rows; their distinct filings are the source's proof filings.
    public virtual ICollection<SourceFieldReview> Reviews { get; set; } = [];
}
