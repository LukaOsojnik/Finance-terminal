using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICountryChallengeRepository
{
    IEnumerable<CountryChallenge> GetAll();
    CountryChallenge? GetById(long id);
    IEnumerable<CountryChallenge> Search(string? term);
    void Add(CountryChallenge entity);
    void Update(CountryChallenge entity);
    void SoftDelete(long id);
}
