using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class RevenueSourceRepository(AppDbContext db)
    : ContributionRepositoryBase<RevenueSource>(db), IRevenueSourceRepository
{
    protected override DbSet<RevenueSource> Set => Db.RevenueSources;

    protected override IQueryable<RevenueSource> ListIncludes(IQueryable<RevenueSource> q) =>
        q.Include(r => r.Company).Include(r => r.RelatedCompany);

    // Filing: the detail page and the extraction page show the row's proof filing.
    protected override IQueryable<RevenueSource> DetailIncludes(IQueryable<RevenueSource> q) =>
        q.Include(r => r.Company).Include(r => r.RelatedCompany).Include(r => r.Filing);

    protected override IQueryable<RevenueSource> PendingFeedIncludes(IQueryable<RevenueSource> q) =>
        q.Include(r => r.Company);

    // Company's review page shows the counterparty, who proposed each pending row, and its proof filing.
    protected override IQueryable<RevenueSource> PendingByCompanyIncludes(IQueryable<RevenueSource> q) =>
        q.Include(r => r.RelatedCompany).Include(r => r.ContributedBy).Include(r => r.Filing);
}
