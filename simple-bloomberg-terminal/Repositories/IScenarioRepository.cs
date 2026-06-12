using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IScenarioRepository
{
    IEnumerable<Scenario> GetAll();
    Scenario? GetById(long id);
    IEnumerable<Scenario> Search(string? term);
    void Add(Scenario entity);
    void Update(Scenario entity);
    void SoftDelete(long id);
}
