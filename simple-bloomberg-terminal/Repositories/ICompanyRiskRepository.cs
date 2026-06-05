using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public interface ICompanyRiskRepository
{
    IEnumerable<CompanyRisk> GetAll();
    CompanyRisk? GetById(long id);
    IEnumerable<CompanyRisk> Search(string? term);
    void Add(CompanyRisk entity);
    void Update(CompanyRisk entity);
    void SoftDelete(long id);
    // Idempotent external-refresh support: soft-delete a company's rows from one source.
    void ClearByCompanyAndDataSource(long companyId, DataSource source);
}
