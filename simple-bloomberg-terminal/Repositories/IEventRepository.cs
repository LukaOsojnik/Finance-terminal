using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IEventRepository
{
    IEnumerable<Event> GetAll();
    Event? GetById(long id);
    IEnumerable<Event> Search(string? term);
    void Add(Event entity, IEnumerable<long> countryIds, IEnumerable<long> companyIds, IEnumerable<long> tradeBlocIds);
    void Update(Event entity, IEnumerable<long> countryIds, IEnumerable<long> companyIds, IEnumerable<long> tradeBlocIds);
    void SoftDelete(long id);
}
