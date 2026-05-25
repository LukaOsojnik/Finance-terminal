using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICountryRepository
{
    IEnumerable<Country> GetAll();
    Country? GetById(long id);
    IEnumerable<Country> Search(string? term);
    IEnumerable<Country> Lookup(string? term);
    void Add(Country entity);
    void Update(Country entity);
    void SoftDelete(long id);
}
