using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public class CostSourceRepository(AppDbContext db) : ICostSourceRepository
{
    public IEnumerable<CostSource> GetAll() =>
        db.CostSources
            .Include(c => c.Company)
            .Include(c => c.RelatedCompany)
            .Where(c => c.DeletedAt == null && c.Status == ContributionStatus.Approved)
            .OrderBy(c => c.Company!.Name).ThenBy(c => c.Name)
            .ToList();

    // Manager review feed: every pending contribution across all companies (light — Company only).
    public IEnumerable<CostSource> GetAllPending() =>
        db.CostSources
            .Include(c => c.Company)
            .Where(c => c.DeletedAt == null && c.Status == ContributionStatus.Pending)
            .OrderBy(c => c.Company!.Name).ThenBy(c => c.Name)
            .ToList();

    // Pending contributions for one company's review page (RelatedCompany + ContributedBy for display).
    public IEnumerable<CostSource> GetPendingByCompany(long companyId) =>
        db.CostSources
            .Include(c => c.RelatedCompany)
            .Include(c => c.ContributedBy)
            .Where(c => c.CompanyId == companyId && c.DeletedAt == null && c.Status == ContributionStatus.Pending)
            .OrderBy(c => c.Name)
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
            .Where(c => c.DeletedAt == null && c.Status == ContributionStatus.Approved);
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

    public void ClearByCompanyAndDataSource(long companyId, DataSource source)
    {
        var rows = db.CostSources
            .Where(c => c.CompanyId == companyId && c.DataSource == source && c.DeletedAt == null)
            .ToList();
        if (rows.Count == 0) return;
        foreach (var c in rows) c.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
