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
            .ToList();

    public Event? GetById(long id) =>
        db.Events
            .Include(e => e.Countries)
            .Include(e => e.Companies)
            .Include(e => e.TradeBlocs)
            .FirstOrDefault(e => e.Id == id);
}
