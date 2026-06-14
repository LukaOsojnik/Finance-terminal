namespace simple_bloomberg_terminal.Models.ViewModels;

// One row on the review index: a company that has pending user contributions, with per-section counts.
public class ContributionCompanyRow
{
    public long CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public int RevenueCount { get; set; }
    public int CostCount { get; set; }
    public int RiskCount { get; set; }
    public int Total => RevenueCount + CostCount + RiskCount;
}

// A company's pending contributions split into the three reviewable sections.
public class CompanyContributionsViewModel
{
    public long CompanyId { get; set; }
    public string CompanyName { get; set; } = "";
    public List<ContributionRow> Revenue { get; set; } = [];
    public List<ContributionRow> Cost { get; set; } = [];
    public List<ContributionRow> Risk { get; set; } = [];
    public int Total => Revenue.Count + Cost.Count + Risk.Count;
}

// One pending row a Manager rules on. Type is the section key ("REVENUE" / "COST" / "RISK") the
// Approve/Reject form posts back so the controller knows which repository to act on.
public class ContributionRow
{
    public string Type { get; set; } = "";
    public long Id { get; set; }
    public string Classification { get; set; } = "";
    public string Name { get; set; } = "";
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public string? Note { get; set; }
    public string? RelatedCompany { get; set; }
    public string? ContributorEmail { get; set; }

    // Set when this is a proposed EDIT of a live row (rather than a new addition): the live row stays
    // public until this is approved, at which point it is soft-deleted in favour of this one.
    public long? SupersedesId { get; set; }
    public string? SupersededName { get; set; }

    // Frozen proof excerpts (+ filing links) the contributor cited, so the Manager can verify before
    // approving. Pulled from the row's SourceFieldReview cells.
    public List<ContributionProof> Proofs { get; set; } = [];
}

// A single per-field proof snapshot shown under a pending row.
public class ContributionProof
{
    public string Field { get; set; } = "";
    public string Snapshot { get; set; } = "";
    public string? Pointer { get; set; }
    public string Endpoint { get; set; } = "";
    public string? FilingLabel { get; set; }
    public string? FilingUrl { get; set; }
}
