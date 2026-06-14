using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public interface ICostSourceRepository
{
    IEnumerable<CostSource> GetAll();
    IEnumerable<CostSource> GetAllPending();
    IEnumerable<CostSource> GetPendingByCompany(long companyId);
    CostSource? GetById(long id);
    IEnumerable<CostSource> Search(string? term);
    void Add(CostSource entity);
    void Update(CostSource entity);
    void SoftDelete(long id);
    // Idempotent external-refresh support: soft-delete a company's rows from one source.
    void ClearByCompanyAndDataSource(long companyId, DataSource source);
}
