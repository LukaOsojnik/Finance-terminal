using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryChallengeRepository(AppDbContext db) : ICountryChallengeRepository
{
    public IEnumerable<CountryChallenge> GetAll() =>
        db.CountryChallenges
            .Include(c => c.Country)
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.Country!.Name)
            .ToList();

    public CountryChallenge? GetById(long id) =>
        db.CountryChallenges
            .Include(c => c.Country)
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    public IEnumerable<CountryChallenge> Search(string? term)
    {
        var q = db.CountryChallenges
            .Include(c => c.Country)
            .Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(c =>
                EF.Functions.Like(c.Text, $"%{t}%") ||
                (c.Country != null && EF.Functions.Like(c.Country.Name, $"%{t}%")));
        }
        return q.OrderBy(c => c.Country!.Name).ToList();
    }

    public void Add(CountryChallenge entity) { db.CountryChallenges.Add(entity); db.SaveChanges(); }
    public void Update(CountryChallenge entity) { db.CountryChallenges.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.CountryChallenges.FirstOrDefault(c => c.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
