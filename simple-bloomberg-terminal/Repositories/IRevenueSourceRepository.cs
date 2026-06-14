using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public interface IRevenueSourceRepository
{
    IEnumerable<RevenueSource> GetAll();
    IEnumerable<RevenueSource> GetAllPending();
    IEnumerable<RevenueSource> GetPendingByCompany(long companyId);
    RevenueSource? GetById(long id);
    IEnumerable<RevenueSource> Search(string? term);
    void Add(RevenueSource entity);
    void Update(RevenueSource entity);
    void SoftDelete(long id);
    // Idempotent external-refresh support: soft-delete a company's rows from one source.
    void ClearByCompanyAndDataSource(long companyId, DataSource source);
}
