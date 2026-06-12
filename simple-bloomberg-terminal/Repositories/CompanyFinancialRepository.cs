using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CompanyFinancialRepository(AppDbContext db) : ICompanyFinancialRepository
{
    public IEnumerable<CompanyFinancial> GetAll() =>
        db.CompanyFinancials
            .Include(f => f.Company)
            .Where(f => f.DeletedAt == null)
            .OrderBy(f => f.Company!.Name).ThenByDescending(f => f.FiscalYear).ThenBy(f => f.Period)
            .ToList();

    public CompanyFinancial? GetById(long id) =>
        db.CompanyFinancials
            .Include(f => f.Company)
            .FirstOrDefault(f => f.Id == id && f.DeletedAt == null);

    public IEnumerable<CompanyFinancial> Search(string? term)
    {
        var q = db.CompanyFinancials
            .Include(f => f.Company)
            .Where(f => f.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            if (int.TryParse(t, out var year))
                q = q.Where(f => f.FiscalYear == year || (f.Company != null && EF.Functions.Like(f.Company.Name, $"%{t}%")));
            else
                q = q.Where(f => f.Company != null && EF.Functions.Like(f.Company.Name, $"%{t}%"));
        }
        return q.OrderBy(f => f.Company!.Name).ThenByDescending(f => f.FiscalYear).ThenBy(f => f.Period).ToList();
    }

    public void Add(CompanyFinancial entity)
    {
        entity.CapturedAt = DateTime.UtcNow;
        db.CompanyFinancials.Add(entity);
        db.SaveChanges();
    }

    public void Update(CompanyFinancial entity) { db.CompanyFinancials.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.CompanyFinancials.FirstOrDefault(f => f.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
