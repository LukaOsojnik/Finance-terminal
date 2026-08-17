using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A risk a company discloses (extracted from Item 1A risk factors / Item 7A market risk). Unlike
/// revenue/cost rows it has no money figures — just a short Name, a <see cref="RiskScope"/> bucket,
/// and a free-text Note. Its proof is the <see cref="Reference"/> / <see cref="Evidence"/> pair on
/// the row itself, taken from <see cref="Filing"/>.
/// </summary>
public class CompanyRisk : IContribution
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

    // WHERE in the document this row came from: the SEC Item / note / subheading (e.g.
    // "Item 1A. Risk Factors"), set by the extraction agent.
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

    [ForeignKey("FilingId")]
    public virtual Filing? Filing { get; set; }
}
