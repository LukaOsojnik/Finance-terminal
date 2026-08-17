using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Extraction;

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
/// cost, risk): creating/editing a row (with the reviewer-gate + supersession rules) together with
/// its proof, mirroring a counterparty link, and the approve/reject transitions. Lives here
/// (not in the controllers) so every revenue/cost/risk write flows through one set of rules.
/// </summary>
public interface IContributionWriter
{
    // Create or update the source row for the active node, returning its id. Null when the
    // classification can't be parsed, or an existing-row id pointed at no row.
    // The proof rides along on the row: <paramref name="reference"/> is WHERE in the document it came
    // from (SEC Item / note / subheading), <paramref name="evidence"/> the verbatim substring, and
    // <paramref name="filingId"/> the filing both were taken from. Each is left untouched when null,
    // so an edit that omits proof keeps the citation already on record.
    long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by,
        string? reference = null, string? evidence = null, long? filingId = null);

    // Create the mirror source on the counterparty pointing back at owner, unless one already exists.
    void EnsureReciprocal(ExtractionNode node, long counterpartyId, long ownerId, string ownerName,
        double? value, Contributor by);

    void Approve(string type, IEnumerable<long> ids);
    void Reject(string type, IEnumerable<long> ids);
}

public class ContributionWriter(
    IRevenueSourceRepository revenue, ICostSourceRepository cost, ICompanyRiskRepository risks)
    : IContributionWriter
{
    // The proof a write carries: where in the document, the verbatim quote, and the filing both came
    // from. Bundled so the three per-node upserts don't each grow three more parameters.
    private readonly record struct Proof(string? Reference, string? Evidence, long? FilingId);

    public long? UpsertRow(ExtractionNode node, long companyId, long? rowId, string classification,
        string name, double? value, double? percentage, string? note, long? relatedCompanyId, Contributor by,
        string? reference = null, string? evidence = null, long? filingId = null)
    {
        var proof = new Proof(reference, evidence, filingId);
        return node switch
        {
            ExtractionNode.COST => UpsertCost(companyId, rowId, classification, name, value, percentage, relatedCompanyId, proof, by),
            ExtractionNode.RISK => UpsertRisk(companyId, rowId, classification, name, note, proof, by),
            _                   => UpsertRevenue(companyId, rowId, classification, name, value, percentage, relatedCompanyId, proof, by),
        };
    }

    private long? UpsertRevenue(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Proof proof, Contributor by)
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
                    Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
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
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            revenue.Update(existing);
            return existing.Id;
        }
        var row = new RevenueSource(sourceType, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        revenue.Add(row);
        return row.Id;
    }

    private long? UpsertCost(long companyId, long? rowId, string classification, string name,
        double? value, double? percentage, long? relatedCompanyId, Proof proof, Contributor by)
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
                    Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
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
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            cost.Update(existing);
            return existing.Id;
        }
        var row = new CostSource(costBase, name, companyId)
        {
            Value = value, Percentage = percentage, RelatedCompanyId = relatedCompanyId,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        cost.Add(row);
        return row.Id;
    }

    private long? UpsertRisk(long companyId, long? rowId, string classification, string name, string? note, Proof proof, Contributor by)
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
                    Note = note,
                    Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
                    DataSource = DataSource.MANUAL,
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
            ApplyProof(proof, r => existing.Reference = r, e => existing.Evidence = e, f => existing.FilingId = f);
            risks.Update(existing);
            return existing.Id;
        }
        var row = new CompanyRisk(scope, name, companyId)
        {
            Note = note,
            Reference = proof.Reference, Evidence = proof.Evidence, FilingId = proof.FilingId,
            DataSource = DataSource.MANUAL,
            Status = by.NewStatus,
            ContributedByUserId = by.StampUserId
        };
        risks.Add(row);
        return row.Id;
    }

    // Write each proof part onto the row it belongs to, skipping the ones the caller omitted — an
    // edit that sends no citation keeps the one already on record.
    private static void ApplyProof(Proof proof, Action<string> setReference, Action<string> setEvidence,
        Action<long> setFilingId)
    {
        if (proof.Reference is not null) setReference(proof.Reference);
        if (proof.Evidence is not null) setEvidence(proof.Evidence);
        if (proof.FilingId is { } filingId) setFilingId(filingId);
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
