using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CostSourceRepository(AppDbContext db) : ICostSourceRepository
{
    public IEnumerable<CostSource> GetAll() =>
        db.CostSources
            .Include(c => c.Company)
            .Include(c => c.RelatedCompany)
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.Company!.Name).ThenBy(c => c.Name)
            .ToList();

    public CostSource? GetById(long id) =>
        db.CostSources
            .Include(c => c.Company)
            .Include(c => c.RelatedCompany)
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    public IEnumerable<CostSource> Search(string? term)
    {
        var q = db.CostSources
            .Include(c => c.Company)
            .Include(c => c.RelatedCompany)
            .Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(c =>
                EF.Functions.Like(c.Name, $"%{t}%") ||
                (c.Company != null && EF.Functions.Like(c.Company.Name, $"%{t}%")));
        }
        return q.OrderBy(c => c.Company!.Name).ThenBy(c => c.Name).ToList();
    }

    public void Add(CostSource entity) { db.CostSources.Add(entity); db.SaveChanges(); }
    public void Update(CostSource entity) { db.CostSources.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.CostSources.FirstOrDefault(c => c.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
