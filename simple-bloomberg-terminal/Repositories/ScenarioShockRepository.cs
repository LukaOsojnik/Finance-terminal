using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class ScenarioShockRepository(AppDbContext db) : IScenarioShockRepository
{
    public IEnumerable<ScenarioShock> GetAll() =>
        db.ScenarioShocks
            .Include(s => s.Scenario)
            .Where(s => s.DeletedAt == null)
            .OrderBy(s => s.ScenarioId)
            .ToList();

    public ScenarioShock? GetById(long id) =>
        db.ScenarioShocks
            .Include(s => s.Scenario)
            .FirstOrDefault(s => s.Id == id && s.DeletedAt == null);

    public IEnumerable<ScenarioShock> Search(string? term)
    {
        var q = db.ScenarioShocks.Include(s => s.Scenario).Where(s => s.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(s => s.Scenario != null && EF.Functions.Like(s.Scenario.Name, $"%{t}%"));
        }
        return q.OrderBy(s => s.ScenarioId).ToList();
    }

    public void Add(ScenarioShock entity) { db.ScenarioShocks.Add(entity); db.SaveChanges(); }
    public void Update(ScenarioShock entity) { db.ScenarioShocks.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.ScenarioShocks.FirstOrDefault(s => s.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
