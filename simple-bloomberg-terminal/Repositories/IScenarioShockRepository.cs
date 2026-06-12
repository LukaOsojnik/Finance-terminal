using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IScenarioShockRepository
{
    IEnumerable<ScenarioShock> GetAll();
    ScenarioShock? GetById(long id);
    IEnumerable<ScenarioShock> Search(string? term);
    void Add(ScenarioShock entity);
    void Update(ScenarioShock entity);
    void SoftDelete(long id);
}
