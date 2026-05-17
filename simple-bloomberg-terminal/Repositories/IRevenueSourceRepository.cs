using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IRevenueSourceRepository
{
    IEnumerable<RevenueSource> GetAll();
    RevenueSource? GetById(long id);
    IEnumerable<RevenueSource> Search(string? term);
    void Add(RevenueSource entity);
    void Update(RevenueSource entity);
    void SoftDelete(long id);
}
