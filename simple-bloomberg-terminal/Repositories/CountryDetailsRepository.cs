using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryDetailsRepository(AppDbContext db) : ICountryDetailsRepository
{
    public IEnumerable<CountryDetails> GetAll() =>
        db.CountryDetails
            .Include(d => d.Country)
            .Where(d => d.DeletedAt == null)
            .OrderBy(d => d.Country!.Name)
            .ToList();

    public CountryDetails? GetById(long countryId) => GetByCountryId(countryId);

    public CountryDetails? GetByCountryId(long countryId) =>
        db.CountryDetails
            .Include(d => d.Country)
            .Include(d => d.Advantages)
            .Include(d => d.Challenges)
            .Include(d => d.GdpHistory)
            .FirstOrDefault(d => d.CountryId == countryId && d.DeletedAt == null);

    public IEnumerable<CountryDetails> Search(string? term)
    {
        var q = db.CountryDetails
            .Include(d => d.Country)
            .Where(d => d.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(d =>
                EF.Functions.Like(d.MarketPosition, $"%{t}%") ||
                (d.Country != null && EF.Functions.Like(d.Country.Name, $"%{t}%")));
        }
        return q.OrderBy(d => d.Country!.Name).ToList();
    }

    public void Add(CountryDetails entity)
    {
        db.CountryDetails.Add(entity);
        db.SaveChanges();
    }

    public void Update(CountryDetails entity)
    {
        db.CountryDetails.Update(entity);
        db.SaveChanges();
    }

    public void SoftDelete(long countryId)
    {
        var entity = db.CountryDetails.FirstOrDefault(d => d.CountryId == countryId);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
