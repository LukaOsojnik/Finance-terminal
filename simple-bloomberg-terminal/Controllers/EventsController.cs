using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("events")]
[Authorize(Roles = "Admin,Manager")]
public class EventsController : Controller
{
    private readonly IEventRepository _events;
    private readonly ICountryRepository _countries;
    private readonly ICompanyRepository _companies;
    private readonly ITradeBlocRepository _tradeBlocs;
    // Today (UTC) for the LIVE/PAST/UPCOMING status of an event's date range.
    private static DateTime Today => DateTime.UtcNow.Date;

    public EventsController(
        IEventRepository events,
        ICountryRepository countries,
        ICompanyRepository companies,
        ITradeBlocRepository tradeBlocs)
    {
        _events = events;
        _countries = countries;
        _companies = companies;
        _tradeBlocs = tradeBlocs;
    }

    [AllowAnonymous]
    [HttpGet, Route("feed")]
    public IActionResult Index() =>
        View(_events.GetAll().Select(ToRow));

    [AllowAnonymous]
    [HttpGet, Route("search")]
    public IActionResult Search(string? term) =>
        PartialView("_TableBody", _events.Search(term).Select(ToRow));

    private static EventRowViewModel ToRow(Event ev)
    {
        var row = EventRowViewModel.From(ev);
        bool isLive = ev.Date.Date <= Today && (ev.EndDate == null || ev.EndDate.Value.Date >= Today);
        bool isPast = ev.EndDate != null && ev.EndDate.Value.Date < Today;
        row.StatusLabel = isLive ? "LIVE" : isPast ? "PAST" : "UPCOMING";
        return row;
    }

    [AllowAnonymous]
    [HttpGet, Route("{id:long}/summary")]
    public IActionResult Details(long id)
    {
        var ev = _events.GetById(id);
        if (ev == null) return NotFound();

        bool isLive = ev.Date.Date <= Today && (ev.EndDate == null || ev.EndDate.Value.Date >= Today);
        bool isPast = ev.EndDate != null && ev.EndDate.Value.Date < Today;

        return View(new EventDetailsViewModel
        {
            Event = ev,
            IsLive = isLive,
            IsPast = isPast,
            StatusLabel = isLive ? "LIVE" : isPast ? "PAST" : "UPCOMING",
            TypeLabel = ev.Type.ToString().Replace("_", " ")
        });
    }

    [HttpGet, Route("create", Name = "EventsCreate")]
    public IActionResult Create()
    {
        PopulateDropdowns();
        return View(new EventCreateModel());
    }

    [HttpPost, Route("create"), ValidateAntiForgeryToken]
    public IActionResult Create(EventCreateModel model)
    {
        if (!ModelState.IsValid) { PopulateDropdowns(); return View(model); }
        var entity = new Event(model.Title, model.Type!.Value, model.Date!.Value)
        {
            EndDate = model.EndDate,
            Description = model.Description,
            ImpactScore = model.ImpactScore
        };
        _events.Add(entity, model.SelectedCountryIds, model.SelectedCompanyIds, model.SelectedTradeBlocIds);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet, ActionName("Edit"), Route("{id:long}/edit")]
    public IActionResult EditGet(long id)
    {
        var entity = _events.GetById(id);
        if (entity == null) return NotFound();
        PopulateDropdowns();
        return View("Edit", ToEditModel(entity));
    }

    [HttpPost, ActionName("Edit"), Route("{id:long}/edit"), ValidateAntiForgeryToken]
    public async Task<IActionResult> EditPost(long id)
    {
        var entity = _events.GetById(id);
        if (entity == null) return NotFound();

        var model = ToEditModel(entity);
        var ok = await TryUpdateModelAsync(model);
        if (!ok || !ModelState.IsValid) { PopulateDropdowns(); return View("Edit", model); }

        ApplyEdit(entity, model);
        _events.Update(entity, model.SelectedCountryIds, model.SelectedCompanyIds, model.SelectedTradeBlocIds);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, Route("{id:long}/delete", Name = "EventDelete"), ValidateAntiForgeryToken]
    public IActionResult Delete(long id)
    {
        try { _events.SoftDelete(id); }
        catch (InvalidOperationException ex) { TempData["Error"] = ex.Message; }
        return RedirectToAction(nameof(Index));
    }

    private void PopulateDropdowns()
    {
        ViewBag.Countries = _countries.GetAll().ToList();
        ViewBag.Companies = _companies.GetAll().ToList();
        ViewBag.TradeBlocs = _tradeBlocs.GetAll().ToList();
        ViewBag.EventTypes = Enum.GetValues<EventType>()
            .Select(t => new SelectListItem(t.ToString().Replace("_", " "), t.ToString())).ToList();
    }

    private static EventEditModel ToEditModel(Event e) => new()
    {
        Id = e.Id,
        Title = e.Title,
        Type = e.Type,
        Date = e.Date,
        EndDate = e.EndDate,
        Description = e.Description,
        ImpactScore = e.ImpactScore,
        SelectedCountryIds = e.Countries.Select(c => c.Id).ToList(),
        SelectedCompanyIds = e.Companies.Select(c => c.Id).ToList(),
        SelectedTradeBlocIds = e.TradeBlocs.Select(t => t.Id).ToList()
    };

    private static void ApplyEdit(Event e, EventEditModel m)
    {
        e.Title = m.Title;
        e.Type = m.Type!.Value;
        e.Date = m.Date!.Value;
        e.EndDate = m.EndDate;
        e.Description = m.Description;
        e.ImpactScore = m.ImpactScore;
    }
}
