using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class TradeBlocRepository(AppDbContext db) : ITradeBlocRepository
{
    public IEnumerable<TradeBloc> GetAll() =>
        db.TradeBlocs
            .Include(t => t.Countries)
            .Where(t => t.DeletedAt == null)
            .OrderBy(t => t.Name)
            .ToList();

    public TradeBloc? GetById(long id) =>
        db.TradeBlocs
            .Include(t => t.Countries)
            .Include(t => t.Events)
            .FirstOrDefault(t => t.Id == id && t.DeletedAt == null);

    public IEnumerable<TradeBloc> Search(string? term)
    {
        var q = db.TradeBlocs
            .Include(t => t.Countries)
            .Where(t => t.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t2 = term.Trim();
            q = q.Where(t =>
                EF.Functions.Like(t.Name, $"%{t2}%") ||
                EF.Functions.Like(t.Code, $"%{t2}%"));
        }
        return q.OrderBy(t => t.Name).ToList();
    }

    public void Add(TradeBloc entity, IEnumerable<long> countryIds)
    {
        db.TradeBlocs.Add(entity);
        ReplaceCountries(entity, countryIds);
        db.SaveChanges();
    }

    public void Update(TradeBloc entity, IEnumerable<long> countryIds)
    {
        ReplaceCountries(entity, countryIds);
        db.TradeBlocs.Update(entity);
        db.SaveChanges();
    }

    public void SoftDelete(long id)
    {
        var entity = db.TradeBlocs.Include(t => t.Countries).FirstOrDefault(t => t.Id == id);
        if (entity == null || entity.DeletedAt != null) return;

        var hasActiveCountries = entity.Countries.Any(c => c.DeletedAt == null);
        if (hasActiveCountries)
            throw new InvalidOperationException("Cannot delete trade bloc: active member countries exist.");

        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    private void ReplaceCountries(TradeBloc entity, IEnumerable<long> countryIds)
    {
        var ids = countryIds?.ToList() ?? new List<long>();
        entity.Countries = db.Countries.Where(c => ids.Contains(c.Id) && c.DeletedAt == null).ToList();
    }
}
