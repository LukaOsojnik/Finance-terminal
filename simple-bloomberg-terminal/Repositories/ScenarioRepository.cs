using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class ScenarioRepository(AppDbContext db) : IScenarioRepository
{
    public IEnumerable<Scenario> GetAll() =>
        db.Scenarios
            .Include(s => s.Shocks.Where(sh => sh.DeletedAt == null))
            .Where(s => s.DeletedAt == null)
            .OrderBy(s => s.Name)
            .ToList();

    public Scenario? GetById(long id) =>
        db.Scenarios
            .Include(s => s.Shocks.Where(sh => sh.DeletedAt == null))
            .FirstOrDefault(s => s.Id == id && s.DeletedAt == null);

    public IEnumerable<Scenario> Search(string? term)
    {
        var q = db.Scenarios.Include(s => s.Shocks.Where(sh => sh.DeletedAt == null)).Where(s => s.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(s =>
                EF.Functions.Like(s.Name, $"%{t}%") ||
                (s.Description != null && EF.Functions.Like(s.Description, $"%{t}%")));
        }
        return q.OrderBy(s => s.Name).ToList();
    }

    public void Add(Scenario entity) { db.Scenarios.Add(entity); db.SaveChanges(); }
    public void Update(Scenario entity) { db.Scenarios.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.Scenarios.FirstOrDefault(s => s.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
