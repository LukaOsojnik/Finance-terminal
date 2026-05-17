using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class GdpSnapshotRepository(AppDbContext db) : IGdpSnapshotRepository
{
    public IEnumerable<GdpSnapshot> GetAll() =>
        db.GdpSnapshots
            .Include(g => g.Country)
            .Where(g => g.DeletedAt == null)
            .OrderByDescending(g => g.Year)
            .ToList();

    public GdpSnapshot? GetById(long id) =>
        db.GdpSnapshots
            .Include(g => g.Country)
            .FirstOrDefault(g => g.Id == id && g.DeletedAt == null);

    public IEnumerable<GdpSnapshot> Search(string? term)
    {
        var q = db.GdpSnapshots
            .Include(g => g.Country)
            .Where(g => g.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            if (int.TryParse(t, out var year))
                q = q.Where(g => g.Year == year || (g.Country != null && EF.Functions.Like(g.Country.Name, $"%{t}%")));
            else
                q = q.Where(g => g.Country != null && EF.Functions.Like(g.Country.Name, $"%{t}%"));
        }
        return q.OrderByDescending(g => g.Year).ToList();
    }

    public void Add(GdpSnapshot entity) { db.GdpSnapshots.Add(entity); db.SaveChanges(); }
    public void Update(GdpSnapshot entity) { db.GdpSnapshots.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.GdpSnapshots.FirstOrDefault(g => g.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
