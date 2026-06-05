using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public class CompanyRiskRepository(AppDbContext db) : ICompanyRiskRepository
{
    public IEnumerable<CompanyRisk> GetAll() =>
        db.CompanyRisks
            .Include(r => r.Company)
            .Where(r => r.DeletedAt == null)
            .OrderBy(r => r.Company!.Name).ThenBy(r => r.Name)
            .ToList();

    public CompanyRisk? GetById(long id) =>
        db.CompanyRisks
            .Include(r => r.Company)
            .FirstOrDefault(r => r.Id == id && r.DeletedAt == null);

    public IEnumerable<CompanyRisk> Search(string? term)
    {
        var q = db.CompanyRisks
            .Include(r => r.Company)
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

    public void Add(CompanyRisk entity) { db.CompanyRisks.Add(entity); db.SaveChanges(); }
    public void Update(CompanyRisk entity) { db.CompanyRisks.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.CompanyRisks.FirstOrDefault(r => r.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    public void ClearByCompanyAndDataSource(long companyId, DataSource source)
    {
        var rows = db.CompanyRisks
            .Where(r => r.CompanyId == companyId && r.DataSource == source && r.DeletedAt == null)
            .ToList();
        if (rows.Count == 0) return;
        foreach (var r in rows) r.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
