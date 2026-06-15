using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

/// <summary>
/// Manager review queue for user-contributed data. Regular users run Perplexity/DeepSeek discovery and
/// extraction; the rows they produce land as <see cref="ContributionStatus.Pending"/> and stay hidden
/// from the public app until a Manager rules on them here. Approve flips the row live (and soft-deletes
/// the row it supersedes, if it's a proposed edit); Reject marks it <see cref="ContributionStatus.Rejected"/>
/// so it vanishes from both the public app and this queue. Manager/Admin only.
/// </summary>
[Route("contributions")]
[Authorize(Roles = "Manager,Admin")]
public class ContributionsController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly IRevenueSourceRepository _revenue;
    private readonly ICostSourceRepository _cost;
    private readonly ICompanyRiskRepository _risks;
    private readonly ISourceFieldReviewRepository _reviews;
    private readonly UserManager<AppUser> _users;
    private readonly IContributionWriter _writer;

    public ContributionsController(
        ICompanyRepository companies, IRevenueSourceRepository revenue, ICostSourceRepository cost,
        ICompanyRiskRepository risks, ISourceFieldReviewRepository reviews, UserManager<AppUser> users,
        IContributionWriter writer)
    {
        _companies = companies;
        _revenue = revenue;
        _cost = cost;
        _risks = risks;
        _reviews = reviews;
        _users = users;
        _writer = writer;
    }

    // Every company with at least one pending contribution, with per-section counts.
    [HttpGet, Route("")]
    public IActionResult Index()
    {
        var rows = new Dictionary<long, ContributionCompanyRow>();

        ContributionCompanyRow Row(Company? c, long companyId)
        {
            if (!rows.TryGetValue(companyId, out var r))
                rows[companyId] = r = new ContributionCompanyRow
                {
                    CompanyId = companyId,
                    CompanyName = c?.Name ?? $"Company #{companyId}"
                };
            return r;
        }

        foreach (var x in _revenue.GetAllPending()) Row(x.Company, x.CompanyId).RevenueCount++;
        foreach (var x in _cost.GetAllPending()) Row(x.Company, x.CompanyId).CostCount++;
        foreach (var x in _risks.GetAllPending()) Row(x.Company, x.CompanyId).RiskCount++;

        return View(rows.Values.OrderByDescending(r => r.Total).ThenBy(r => r.CompanyName).ToList());
    }

    // One company's pending contributions, grouped into the three sections with proofs attached.
    [HttpGet, Route("company/{id:long}")]
    public IActionResult Company(long id)
    {
        var company = _companies.GetById(id);
        if (company is null) return NotFound();

        // Proofs for every pending row, fetched once and matched by source id below. Reused from the
        // extraction flow's review repository (Filing already eager-loaded for the link).
        var reviews = _reviews.GetByCompany(id).ToList();

        // Materialize each section once so the projections (and the email batch below) don't re-query.
        var revenue = _revenue.GetPendingByCompany(id).ToList();
        var cost = _cost.GetPendingByCompany(id).ToList();
        var risk = _risks.GetPendingByCompany(id).ToList();

        // Contributor emails for every distinct user across the three sections, in one query (was a
        // blocking FindByIdAsync per distinct user — an N+1).
        var userIds = revenue.Select(r => r.ContributedByUserId)
            .Concat(cost.Select(c => c.ContributedByUserId))
            .Concat(risk.Select(r => r.ContributedByUserId))
            .Where(uid => !string.IsNullOrEmpty(uid))
            .Distinct()
            .ToList();
        var emails = _users.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionary(u => u.Id, u => u.Email);
        string? Email(string? userId) =>
            userId is not null && emails.TryGetValue(userId, out var e) ? e : null;

        var vm = new CompanyContributionsViewModel { CompanyId = id, CompanyName = company.Name };

        vm.Revenue = revenue.Select(r => new ContributionRow
        {
            Type = "REVENUE", Id = r.Id, Classification = r.SourceType.ToString(), Name = r.Name,
            Value = r.Value, Percentage = r.Percentage, RelatedCompany = r.RelatedCompany?.Name,
            ContributorEmail = Email(r.ContributedByUserId),
            SupersedesId = r.SupersedesId,
            SupersededName = r.SupersedesId is { } sid ? _revenue.GetById(sid)?.Name : null,
            Proofs = ProofsFor(reviews, p => p.RevenueSourceId == r.Id)
        }).ToList();

        vm.Cost = cost.Select(c => new ContributionRow
        {
            Type = "COST", Id = c.Id, Classification = c.CostBase.ToString(), Name = c.Name,
            Value = c.Value, Percentage = c.Percentage, RelatedCompany = c.RelatedCompany?.Name,
            ContributorEmail = Email(c.ContributedByUserId),
            SupersedesId = c.SupersedesId,
            SupersededName = c.SupersedesId is { } sid ? _cost.GetById(sid)?.Name : null,
            Proofs = ProofsFor(reviews, p => p.CostSourceId == c.Id)
        }).ToList();

        vm.Risk = risk.Select(r => new ContributionRow
        {
            Type = "RISK", Id = r.Id, Classification = r.Scope.ToString(), Name = r.Name, Note = r.Note,
            ContributorEmail = Email(r.ContributedByUserId),
            SupersedesId = r.SupersedesId,
            SupersededName = r.SupersedesId is { } sid ? _risks.GetById(sid)?.Name : null,
            Proofs = ProofsFor(reviews, p => p.CompanyRiskId == r.Id)
        }).ToList();

        return View(vm);
    }

    private static List<ContributionProof> ProofsFor(
        IEnumerable<SourceFieldReview> reviews, Func<SourceFieldReview, bool> belongsTo) =>
        reviews.Where(belongsTo).Select(p => new ContributionProof
        {
            Field = p.Field.ToString(),
            Snapshot = p.ReferenceSnapshot,
            Pointer = p.ReferencePointer,
            Endpoint = p.Endpoint,
            FilingLabel = p.Filing == null ? null : $"{p.Filing.Form} {p.Filing.AccessionNumber}".Trim(),
            FilingUrl = p.Filing?.PrimaryDocUrl
        }).ToList();

    // Approve every selected pending row. Batched so a section "Approve all" button posts the whole
    // section's ids in one call; the transition rules live in IContributionWriter.
    [HttpPost, Route("approve"), ValidateAntiForgeryToken]
    public IActionResult Approve(string type, long[] ids, long companyId)
    {
        _writer.Approve(type, ids ?? []);
        return RedirectToAction(nameof(Company), new { id = companyId });
    }

    // Reject every selected pending row.
    [HttpPost, Route("reject"), ValidateAntiForgeryToken]
    public IActionResult Reject(string type, long[] ids, long companyId)
    {
        _writer.Reject(type, ids ?? []);
        return RedirectToAction(nameof(Company), new { id = companyId });
    }
}
