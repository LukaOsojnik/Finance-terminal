using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICostSourceRepository
{
    IEnumerable<CostSource> GetAll();
    CostSource? GetById(long id);
    IEnumerable<CostSource> Search(string? term);
    void Add(CostSource entity);
    void Update(CostSource entity);
    void SoftDelete(long id);
}
