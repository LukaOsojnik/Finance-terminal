using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class RevenueSource : IContribution
{
    public RevenueSource(SourceType sourceType, string name, long companyId)
    {
        SourceType = sourceType;
        Name = name;
        CompanyId = companyId;
    }

    [Key]
    public long Id { get; set; }
    public SourceType SourceType { get; set; }
    public string Name { get; set; }
    public double? Value { get; set; }
    public double? Percentage { get; set; }

    // WHERE in the document this row came from: the SEC Item / note / subheading (e.g.
    // "Item 7. Management's Discussion and Analysis"), set by the extraction agent.
    public string? Reference { get; set; }

    // The exact verbatim substring from the filing backing this row — findable by a literal search
    // in the document. One quote per row (the proof used to be split per field; the model only ever
    // produced one).
    public string? Evidence { get; set; }

    // The filing the Reference/Evidence were taken from (null when they came from Company Facts or
    // a web source rather than a filing document).
    public long? FilingId { get; set; }

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

    [ForeignKey("FilingId")]
    public virtual Filing? Filing { get; set; }
}
