using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CostSourceRepository(AppDbContext db)
    : ContributionRepositoryBase<CostSource>(db), ICostSourceRepository
{
    protected override DbSet<CostSource> Set => Db.CostSources;

    protected override IQueryable<CostSource> ListIncludes(IQueryable<CostSource> q) =>
        q.Include(c => c.Company).Include(c => c.RelatedCompany);

    // Filing: the extraction page shows the row's proof filing.
    protected override IQueryable<CostSource> DetailIncludes(IQueryable<CostSource> q) =>
        q.Include(c => c.Company).Include(c => c.RelatedCompany).Include(c => c.Filing);

    protected override IQueryable<CostSource> PendingFeedIncludes(IQueryable<CostSource> q) =>
        q.Include(c => c.Company);

    // Company's review page shows the counterparty, who proposed each pending row, and its proof filing.
    protected override IQueryable<CostSource> PendingByCompanyIncludes(IQueryable<CostSource> q) =>
        q.Include(c => c.RelatedCompany).Include(c => c.ContributedBy).Include(c => c.Filing);
}
