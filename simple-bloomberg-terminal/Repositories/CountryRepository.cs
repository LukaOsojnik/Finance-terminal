using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryRepository(AppDbContext db) : ICountryRepository
{
    public IEnumerable<Country> GetAll() =>
        db.Countries
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .ToList();

    public Country? GetById(long id) =>
        db.Countries
            .Include(c => c.TradeBlocs)
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    public IEnumerable<Country> Search(string? term)
    {
        var q = db.Countries.Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(c =>
                EF.Functions.Like(c.Name, $"%{t}%") ||
                EF.Functions.Like(c.Code, $"%{t}%") ||
                EF.Functions.Like(c.Region, $"%{t}%"));
        }
        return q.OrderBy(c => c.Name).ToList();
    }

    public void Add(Country entity)
    {
        db.Countries.Add(entity);
        db.SaveChanges();
    }

    public void Update(Country entity)
    {
        db.Countries.Update(entity);
        db.SaveChanges();
    }

    public void SoftDelete(long id)
    {
        var entity = db.Countries.FirstOrDefault(c => c.Id == id);
        if (entity == null || entity.DeletedAt != null) return;

        var hasActiveCompanies = db.Companies.Any(x => x.CountryId == id && x.DeletedAt == null);
        if (hasActiveCompanies)
            throw new InvalidOperationException("Cannot delete country: active companies exist.");

        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
