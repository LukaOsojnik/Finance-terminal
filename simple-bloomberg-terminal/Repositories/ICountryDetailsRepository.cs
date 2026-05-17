using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICountryDetailsRepository
{
    IEnumerable<CountryDetails> GetAll();
    CountryDetails? GetById(long countryId);
    CountryDetails? GetByCountryId(long countryId);
    IEnumerable<CountryDetails> Search(string? term);
    void Add(CountryDetails entity);
    void Update(CountryDetails entity);
    void SoftDelete(long countryId);
}
