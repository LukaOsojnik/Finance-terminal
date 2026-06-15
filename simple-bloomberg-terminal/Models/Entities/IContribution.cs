using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// Shared shape of the three company-attached, contribution-reviewed source rows
/// (<see cref="RevenueSource"/>, <see cref="CostSource"/>, <see cref="CompanyRisk"/>).
/// Lets <see cref="Repositories.ContributionRepositoryBase{T}"/> own the soft-delete +
/// Approved/Pending filtering, Name/Company search, and clear-by-source logic once, so the
/// invariant can't drift between the three repositories.
/// </summary>
public interface IContribution
{
    long Id { get; }
    string Name { get; }
    long CompanyId { get; }
    DataSource? DataSource { get; }
    DateTime? DeletedAt { get; set; }
    ContributionStatus Status { get; }
    Company? Company { get; }
}
