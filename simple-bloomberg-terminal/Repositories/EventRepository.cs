using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class EventRepository(AppDbContext db) : IEventRepository
{
    public IEnumerable<Event> GetAll() => db.Events.ToList();

    public Event? GetById(long id) =>
        db.Events.FirstOrDefault(e => e.Id == id);
}
