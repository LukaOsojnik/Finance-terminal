using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class EventRepository(AppDbContext db) : IEventRepository
{
    public IEnumerable<Event> GetAll() =>
        db.Events
            .Include(e => e.Countries)
            .Include(e => e.Companies)
            .Where(e => e.DeletedAt == null)
            .OrderByDescending(e => e.Date)
            .ToList();

    public Event? GetById(long id) =>
        db.Events
            .Include(e => e.Countries)
            .Include(e => e.Companies)
            .Include(e => e.TradeBlocs)
            .FirstOrDefault(e => e.Id == id && e.DeletedAt == null);

    public IEnumerable<Event> Search(string? term)
    {
        var q = db.Events
            .Include(e => e.Countries)
            .Include(e => e.Companies)
            .Where(e => e.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(e =>
                EF.Functions.Like(e.Title, $"%{t}%") ||
                (e.Description != null && EF.Functions.Like(e.Description, $"%{t}%")));
        }
        return q.OrderByDescending(e => e.Date).ToList();
    }

    public void Add(Event entity, IEnumerable<long> countryIds, IEnumerable<long> companyIds, IEnumerable<long> tradeBlocIds)
    {
        db.Events.Add(entity);
        ReplaceLinks(entity, countryIds, companyIds, tradeBlocIds);
        db.SaveChanges();
    }

    public void Update(Event entity, IEnumerable<long> countryIds, IEnumerable<long> companyIds, IEnumerable<long> tradeBlocIds)
    {
        ReplaceLinks(entity, countryIds, companyIds, tradeBlocIds);
        db.Events.Update(entity);
        db.SaveChanges();
    }

    public void SoftDelete(long id)
    {
        var entity = db.Events.FirstOrDefault(e => e.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    private void ReplaceLinks(Event entity, IEnumerable<long> countryIds, IEnumerable<long> companyIds, IEnumerable<long> tradeBlocIds)
    {
        var cIds = countryIds?.ToList() ?? new List<long>();
        var coIds = companyIds?.ToList() ?? new List<long>();
        var tbIds = tradeBlocIds?.ToList() ?? new List<long>();

        entity.Countries = db.Countries.Where(c => cIds.Contains(c.Id) && c.DeletedAt == null).ToList();
        entity.Companies = db.Companies.Where(c => coIds.Contains(c.Id) && c.DeletedAt == null).ToList();
        entity.TradeBlocs = db.TradeBlocs.Where(t => tbIds.Contains(t.Id) && t.DeletedAt == null).ToList();
    }
}
