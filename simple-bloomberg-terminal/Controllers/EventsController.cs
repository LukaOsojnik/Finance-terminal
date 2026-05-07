using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("events")]
public class EventsController : Controller
{
    private readonly IEventRepository _events;
    private static readonly DateOnly Today = new(2026, 4, 16);

    public EventsController(IEventRepository events)
    {
        _events = events;
    }

    [Route("feed")]
    public IActionResult Index()
    {
        IEnumerable<Event> all = _events.GetAll();

        // Live: started but no end date, OR end date is in the future
        IEnumerable<Event> live = all.Where(e =>
            e.Date <= Today && (e.EndDate == null || e.EndDate >= Today));

        // Past: has an end date that already passed
        IEnumerable<Event> past = all.Where(e =>
            e.EndDate != null && e.EndDate < Today);

        return View(new EventsIndexViewModel {
            LiveEvents = live.Select(EventRowViewModel.From),
            PastEvents = past.Select(EventRowViewModel.From)
        });
    }

    [Route("{id:long}/summary")]
    public IActionResult Details(long id)
    {
        var ev = _events.GetById(id);
        if (ev == null) return NotFound();

        bool isLive = ev.Date <= Today && (ev.EndDate == null || ev.EndDate >= Today);
        bool isPast = ev.EndDate != null && ev.EndDate < Today;

        var vm = new EventDetailsViewModel
        {
            Event = ev,
            IsLive = isLive,
            IsPast = isPast,
            StatusLabel = isLive ? "LIVE" : isPast ? "PAST" : "UPCOMING",
            TypeLabel = ev.Type.ToString().Replace("_", " ")
        };

        return View(vm);
    }
}
