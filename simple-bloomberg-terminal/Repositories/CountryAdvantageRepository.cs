using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryAdvantageRepository(AppDbContext db) : ICountryAdvantageRepository
{
    public IEnumerable<CountryAdvantage> GetAll() =>
        db.CountryAdvantages
            .Include(a => a.Country)
            .Where(a => a.DeletedAt == null)
            .OrderBy(a => a.Country!.Name)
            .ToList();

    public CountryAdvantage? GetById(long id) =>
        db.CountryAdvantages
            .Include(a => a.Country)
            .FirstOrDefault(a => a.Id == id && a.DeletedAt == null);

    public IEnumerable<CountryAdvantage> Search(string? term)
    {
        var q = db.CountryAdvantages
            .Include(a => a.Country)
            .Where(a => a.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(a =>
                EF.Functions.Like(a.Text, $"%{t}%") ||
                (a.Country != null && EF.Functions.Like(a.Country.Name, $"%{t}%")));
        }
        return q.OrderBy(a => a.Country!.Name).ToList();
    }

    public void Add(CountryAdvantage entity) { db.CountryAdvantages.Add(entity); db.SaveChanges(); }
    public void Update(CountryAdvantage entity) { db.CountryAdvantages.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.CountryAdvantages.FirstOrDefault(a => a.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
