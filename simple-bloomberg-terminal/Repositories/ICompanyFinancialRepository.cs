using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICompanyFinancialRepository
{
    IEnumerable<CompanyFinancial> GetAll();
    CompanyFinancial? GetById(long id);
    IEnumerable<CompanyFinancial> Search(string? term);
    void Add(CompanyFinancial entity);
    void Update(CompanyFinancial entity);
    void SoftDelete(long id);
}
