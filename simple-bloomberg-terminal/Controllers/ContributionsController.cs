using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

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

    public ContributionsController(
        ICompanyRepository companies, IRevenueSourceRepository revenue, ICostSourceRepository cost,
        ICompanyRiskRepository risks, ISourceFieldReviewRepository reviews, UserManager<AppUser> users)
    {
        _companies = companies;
        _revenue = revenue;
        _cost = cost;
        _risks = risks;
        _reviews = reviews;
        _users = users;
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
        // Contributor email lookup once per distinct user, so the view shows "who" without N queries.
        var emails = new Dictionary<string, string?>();
        string? Email(string? userId)
        {
            if (string.IsNullOrEmpty(userId)) return null;
            if (!emails.TryGetValue(userId, out var e))
                emails[userId] = e = _users.FindByIdAsync(userId).GetAwaiter().GetResult()?.Email;
            return e;
        }

        var vm = new CompanyContributionsViewModel { CompanyId = id, CompanyName = company.Name };

        vm.Revenue = _revenue.GetPendingByCompany(id).Select(r => new ContributionRow
        {
            Type = "REVENUE", Id = r.Id, Classification = r.SourceType.ToString(), Name = r.Name,
            Value = r.Value, Percentage = r.Percentage, RelatedCompany = r.RelatedCompany?.Name,
            ContributorEmail = Email(r.ContributedByUserId),
            SupersedesId = r.SupersedesId,
            SupersededName = r.SupersedesId is { } sid ? _revenue.GetById(sid)?.Name : null,
            Proofs = ProofsFor(reviews, p => p.RevenueSourceId == r.Id)
        }).ToList();

        vm.Cost = _cost.GetPendingByCompany(id).Select(c => new ContributionRow
        {
            Type = "COST", Id = c.Id, Classification = c.CostBase.ToString(), Name = c.Name,
            Value = c.Value, Percentage = c.Percentage, RelatedCompany = c.RelatedCompany?.Name,
            ContributorEmail = Email(c.ContributedByUserId),
            SupersedesId = c.SupersedesId,
            SupersededName = c.SupersedesId is { } sid ? _cost.GetById(sid)?.Name : null,
            Proofs = ProofsFor(reviews, p => p.CostSourceId == c.Id)
        }).ToList();

        vm.Risk = _risks.GetPendingByCompany(id).Select(r => new ContributionRow
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

    // Approve every selected pending row: a proposed edit soft-deletes the live row it supersedes,
    // then the pending row flips to Approved and goes public. Batched so a section "Approve all"
    // button posts the whole section's ids in one call.
    [HttpPost, Route("approve"), ValidateAntiForgeryToken]
    public IActionResult Approve(string type, long[] ids, long companyId)
    {
        foreach (var id in ids ?? [])
        {
            switch (type)
            {
                case "REVENUE" when _revenue.GetById(id) is { Status: ContributionStatus.Pending } r:
                    if (r.SupersedesId is { } rs) _revenue.SoftDelete(rs);
                    r.Status = ContributionStatus.Approved; _revenue.Update(r);
                    break;
                case "COST" when _cost.GetById(id) is { Status: ContributionStatus.Pending } c:
                    if (c.SupersedesId is { } cs) _cost.SoftDelete(cs);
                    c.Status = ContributionStatus.Approved; _cost.Update(c);
                    break;
                case "RISK" when _risks.GetById(id) is { Status: ContributionStatus.Pending } k:
                    if (k.SupersedesId is { } ks) _risks.SoftDelete(ks);
                    k.Status = ContributionStatus.Approved; _risks.Update(k);
                    break;
            }
        }
        return RedirectToAction(nameof(Company), new { id = companyId });
    }

    // Reject every selected pending row: mark Rejected so it leaves both the public app (reads filter
    // Approved) and this queue (reads filter Pending). The live row a rejected edit targeted is left
    // untouched — nothing was ever swapped.
    [HttpPost, Route("reject"), ValidateAntiForgeryToken]
    public IActionResult Reject(string type, long[] ids, long companyId)
    {
        foreach (var id in ids ?? [])
        {
            switch (type)
            {
                case "REVENUE" when _revenue.GetById(id) is { Status: ContributionStatus.Pending } r:
                    r.Status = ContributionStatus.Rejected; _revenue.Update(r);
                    break;
                case "COST" when _cost.GetById(id) is { Status: ContributionStatus.Pending } c:
                    c.Status = ContributionStatus.Rejected; _cost.Update(c);
                    break;
                case "RISK" when _risks.GetById(id) is { Status: ContributionStatus.Pending } k:
                    k.Status = ContributionStatus.Rejected; _risks.Update(k);
                    break;
            }
        }
        return RedirectToAction(nameof(Company), new { id = companyId });
    }
}
