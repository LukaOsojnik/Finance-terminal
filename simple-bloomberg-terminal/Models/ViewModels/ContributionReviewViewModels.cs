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

    // The row's frozen proof, so the Manager can verify before approving: where in the document it
    // came from, the verbatim quote, and a link to the source filing (or the web page, when the row
    // came from discovery and Reference is a URL).
    public string? Reference { get; set; }
    public string? Evidence { get; set; }
    public string? FilingLabel { get; set; }
    public string? FilingUrl { get; set; }
}
