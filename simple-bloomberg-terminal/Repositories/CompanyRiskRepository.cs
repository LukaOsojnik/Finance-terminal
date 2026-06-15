using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CompanyRiskRepository(AppDbContext db)
    : ContributionRepositoryBase<CompanyRisk>(db), ICompanyRiskRepository
{
    protected override DbSet<CompanyRisk> Set => Db.CompanyRisks;

    protected override IQueryable<CompanyRisk> ListIncludes(IQueryable<CompanyRisk> q) =>
        q.Include(r => r.Company);

    protected override IQueryable<CompanyRisk> DetailIncludes(IQueryable<CompanyRisk> q) =>
        q.Include(r => r.Company);

    protected override IQueryable<CompanyRisk> PendingFeedIncludes(IQueryable<CompanyRisk> q) =>
        q.Include(r => r.Company);

    // Company's review page shows who proposed each pending row.
    protected override IQueryable<CompanyRisk> PendingByCompanyIncludes(IQueryable<CompanyRisk> q) =>
        q.Include(r => r.ContributedBy);
}
