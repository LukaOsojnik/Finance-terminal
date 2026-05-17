using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICountryAdvantageRepository
{
    IEnumerable<CountryAdvantage> GetAll();
    CountryAdvantage? GetById(long id);
    IEnumerable<CountryAdvantage> Search(string? term);
    void Add(CountryAdvantage entity);
    void Update(CountryAdvantage entity);
    void SoftDelete(long id);
}
