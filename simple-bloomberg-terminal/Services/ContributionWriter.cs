using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>Who is writing a contribution: a Manager/Admin's writes go live (Approved), everyone
/// else's are held Pending and stamped with the contributor for a Manager to review. The controller
/// derives this from the request (role + user id) and passes it in, so the writer stays HTTP-free.</summary>
public readonly record struct Contributor(bool IsReviewer, string? UserId)
{
    public ContributionStatus NewStatus => IsReviewer ? ContributionStatus.Approved : ContributionStatus.Pending;
    // A new live row carries no contributor; a pending proposal records who proposed it.
    public string? StampUserId => IsReviewer ? null : UserId;
}

/// <summary>
/// Owns the contribution write + review state machine for the three reviewed source types (revenue,
/// cost, risk): creating/editing a row (with the reviewer-gate + supersession rules), upserting its
/// per-field proof, mirroring a counterparty link, and the approve/reject transitions. Lives here
/// (not in the controllers) so every revenue/cost/risk write flows through one set of rules.
/// </summary>
public interface IContributionWriter
{
    // Create or update the source row for the active node, returning its id. Null when the
    // classification can't be parsed, or an existing-row id pointed at no row.
    long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by);

    // One current proof per (row, field) — upsert, not blind insert. A new proof clears any prior
    // phase-2 verdict (stale-pass guard).
    SourceFieldReview UpsertReview(ExtractionNode node, long companyId, long rowId, ReviewableField field,
        string endpoint, string pointer, string snapshot, string? referencedValue, long? filingId);

    // Create the mirror source on the counterparty pointing back at owner, unless one already exists.
    void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName,
        double? value, Contributor by);

    void Approve(string type, IEnumerable<long> ids);
    void Reject(string type, IEnumerable<long> ids);
}

public class ContributionWriter(
    IRevenueSourceRepository revenue, ICostSourceRepository cost, ICompanyRiskRepository risks,
    ISourceFieldReviewRepository reviews)
    : IContributionWriter
{
    public long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by) => node switch
    {
        ExtractionNode.COST => UpsertCost(companyId, rowId, classification, name, value, percentage, relatedCompanyId, by),
        ExtractionNode.RISK => UpsertRisk(companyId, rowId, classification, name, note, by),
        _                   => UpsertRevenue(companyId, rowId, classification, name, value, percentage, relatedCompanyId, by),
    };

    private long? UpsertRevenue(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Contributor by)
    {
        if (!Enum.TryParse<SourceType>(classification, out var sourceType)) return null;
        if (rowId is { } id)
        {
            var existing = revenue.GetById(id);
            if (existing is null) return null;
            // Non-reviewer edit: leave the live row untouched and propose a superseding Pending copy
            // (approved on review -> the old row is soft-deleted). Reviewers edit in place.
            if (!by.IsReviewer)
            {
                var proposal = new RevenueSource(sourceType, name, companyId)
                {
                    Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
                    DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                revenue.Add(proposal);
                return proposal.Id;
            }
            existing.SourceType = sourceType;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            revenue.Update(existing);
            return existing.Id;
        }
        var row = new RevenueSource(sourceType, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        revenue.Add(row);
        return row.Id;
    }

    private long? UpsertCost(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Contributor by)
    {
        if (!Enum.TryParse<CostBase>(classification, out var costBase)) return null;
        if (rowId is { } id)
        {
            var existing = cost.GetById(id);
            if (existing is null) return null;
            // Non-reviewer edit: propose a superseding Pending copy, leave the live row untouched.
            if (!by.IsReviewer)
            {
                var proposal = new CostSource(costBase, name, companyId)
                {
                    Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
                    DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                cost.Add(proposal);
                return proposal.Id;
            }
            existing.CostBase = costBase;
            existing.Name = name;
            existing.Value = value;
            existing.Percentage = percentage;
            existing.RelatedCompanyId = relatedCompanyId;
            cost.Update(existing);
            return existing.Id;
        }
        var row = new CostSource(costBase, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        cost.Add(row);
        return row.Id;
    }

    private long? UpsertRisk(long companyId, long? rowId, string classification, string name, string? note, Contributor by)
    {
        if (!Enum.TryParse<RiskScope>(classification, out var scope)) return null;
        if (rowId is { } id)
        {
            var existing = risks.GetById(id);
            if (existing is null) return null;
            // Non-reviewer edit: propose a superseding Pending copy, leave the live row untouched.
            if (!by.IsReviewer)
            {
                var proposal = new CompanyRisk(scope, name, companyId)
                {
                    Note = note, DataSource = DataSource.MANUAL,
                    Status = ContributionStatus.Pending,
                    ContributedByUserId = by.UserId,
                    SupersedesId = existing.Id
                };
                risks.Add(proposal);
                return proposal.Id;
            }
            existing.Scope = scope;
            existing.Name = name;
            existing.Note = note;
            risks.Update(existing);
            return existing.Id;
        }
        var row = new CompanyRisk(scope, name, companyId)
        {
            Note = note, DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        risks.Add(row);
        return row.Id;
    }

    // Does this review belong to the given node's row?
    private static bool MatchesRow(SourceFieldReview r, ExtractionNode node, long rowId) => node switch
    {
        ExtractionNode.COST => r.CostSourceId == rowId,
        ExtractionNode.RISK => r.CompanyRiskId == rowId,
        _                   => r.RevenueSourceId == rowId,
    };

    public SourceFieldReview UpsertReview(ExtractionNode node, long companyId, long rowId, ReviewableField field,
        string endpoint, string pointer, string snapshot, string? referencedValue, long? filingId)
    {
        var review = reviews.GetByCompany(companyId)
            .FirstOrDefault(r => MatchesRow(r, node, rowId) && r.Field == field);
        if (review is null)
        {
            review = new SourceFieldReview
            {
                CompanyId = companyId,
                Relation = node switch
                {
                    ExtractionNode.COST => RelationKind.COST,
                    ExtractionNode.RISK => RelationKind.RISK,
                    _                   => RelationKind.REVENUE,
                },
                RevenueSourceId = node == ExtractionNode.REVENUE ? rowId : null,
                CostSourceId    = node == ExtractionNode.COST ? rowId : null,
                CompanyRiskId   = node == ExtractionNode.RISK ? rowId : null,
                Field = field,
                Endpoint = endpoint,
                ReferencePointer = pointer,
                ReferenceSnapshot = snapshot,
                ReferencedValue = referencedValue,
                FilingId = filingId
            };
            reviews.Add(review);
        }
        else
        {
            review.Endpoint = endpoint;
            review.ReferencePointer = pointer;
            review.ReferenceSnapshot = snapshot;
            review.ReferencedValue = referencedValue;
            review.FilingId = filingId;
            review.Mark = null;
            review.Rationale = null;
            review.ReviewedAt = null;
            review.ReviewerModel = null;
            reviews.Update(review);
        }
        return review;
    }

    public void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName,
        double? value, Contributor by)
    {
        var (mirror, classification) = node == ExtractionNode.COST
            ? (ExtractionNode.REVENUE, nameof(SourceType.CUSTOMER))
            : (ExtractionNode.COST, nameof(CostBase.COGS));

        var exists = mirror == ExtractionNode.COST
            ? cost.GetAll().Any(c => c.CompanyId == counterpartyId && c.RelatedCompanyId == ownerId)
            : revenue.GetAll().Any(r => r.CompanyId == counterpartyId && r.RelatedCompanyId == ownerId);
        if (exists) return;

        UpsertRow(mirror, counterpartyId, null, classification, ownerName,
            value: value, percentage: null, note: null, relatedCompanyId: ownerId, by);
    }

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
