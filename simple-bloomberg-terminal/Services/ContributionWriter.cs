using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Owns the contribution review state machine for the three reviewed source types (revenue, cost,
/// risk): approving a pending row flips it live and soft-deletes the row it supersedes; rejecting
/// marks it Rejected. Lives here (not in the controller) so the transition rules have one home.
/// </summary>
public interface IContributionWriter
{
    void Approve(string type, IEnumerable<long> ids);
    void Reject(string type, IEnumerable<long> ids);
}

public class ContributionWriter(
    IRevenueSourceRepository revenue, ICostSourceRepository cost, ICompanyRiskRepository risks)
    : IContributionWriter
{
    public void Approve(string type, IEnumerable<long> ids)
    {
        switch (type)
        {
            case "REVENUE": Approve(ids, revenue.GetById, revenue.SoftDelete, revenue.Update); break;
            case "COST": Approve(ids, cost.GetById, cost.SoftDelete, cost.Update); break;
            case "RISK": Approve(ids, risks.GetById, risks.SoftDelete, risks.Update); break;
        }
    }

    public void Reject(string type, IEnumerable<long> ids)
    {
        switch (type)
        {
            case "REVENUE": Reject(ids, revenue.GetById, revenue.Update); break;
            case "COST": Reject(ids, cost.GetById, cost.Update); break;
            case "RISK": Reject(ids, risks.GetById, risks.Update); break;
        }
    }

    // A proposed edit soft-deletes the live row it supersedes, then the pending row flips Approved and
    // goes public. Non-pending ids are skipped, so a double-submit is idempotent.
    private static void Approve<T>(
        IEnumerable<long> ids, Func<long, T?> getById, Action<long> softDelete, Action<T> update)
        where T : IContribution
    {
        foreach (var id in ids)
            if (getById(id) is { Status: ContributionStatus.Pending } row)
            {
                if (row.SupersedesId is { } supersededId) softDelete(supersededId);
                row.Status = ContributionStatus.Approved;
                update(row);
            }
    }

    // Mark a pending row Rejected so it leaves both the public app (reads filter Approved) and the
    // review queue (reads filter Pending). The live row a rejected edit targeted is left untouched —
    // nothing was ever swapped.
    private static void Reject<T>(IEnumerable<long> ids, Func<long, T?> getById, Action<T> update)
        where T : IContribution
    {
        foreach (var id in ids)
            if (getById(id) is { Status: ContributionStatus.Pending } row)
            {
                row.Status = ContributionStatus.Rejected;
                update(row);
            }
    }
}
