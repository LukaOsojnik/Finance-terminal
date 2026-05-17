using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class RevenueSourceRepository(AppDbContext db) : IRevenueSourceRepository
{
    public IEnumerable<RevenueSource> GetAll() =>
        db.RevenueSources
            .Include(r => r.Company)
            .Include(r => r.RelatedCompany)
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.Company!.Name).ThenBy(r => r.Name)
            .ToList();

    public RevenueSource? GetById(long id) =>
        db.RevenueSources
            .Include(r => r.Company)
            .Include(r => r.RelatedCompany)
            .FirstOrDefault(r => r.Id == id && r.DeletedAt == null);

    public IEnumerable<RevenueSource> Search(string? term)
    {
        var q = db.RevenueSources
            .Include(r => r.Company)
            .Include(r => r.RelatedCompany)
            .Where(r => r.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(r =>
                EF.Functions.Like(r.Name, $"%{t}%") ||
                (r.Company != null && EF.Functions.Like(r.Company.Name, $"%{t}%")));
        }
        return q.OrderBy(r => r.Company!.Name).ThenBy(r => r.Name).ToList();
    }

    public void Add(RevenueSource entity) { db.RevenueSources.Add(entity); db.SaveChanges(); }
    public void Update(RevenueSource entity) { db.RevenueSources.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.RevenueSources.FirstOrDefault(r => r.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
