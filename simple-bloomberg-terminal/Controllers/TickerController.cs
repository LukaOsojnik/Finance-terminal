using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Controllers;

[Route("api/ticker")]
public class TickerController : Controller
{
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;
    private readonly IEventRepository _events;

    public TickerController(
        ICompanyRepository companies,
        ICountryRepository countries,
        IEventRepository events)
    {
        _companies = companies;
        _countries = countries;
        _events = events;
    }

    [HttpGet, Route("feed")]
    public IActionResult Feed(string? exclude = null)
    {
        var skip = exclude?.ToLowerInvariant();

        var events = skip == "events" ? Enumerable.Empty<TickerItem>() : _events.GetAll()
            .OrderByDescending(e => e.Date)
            .Take(8)
            .Select(e => new TickerItem(
                Kind:  "EVT",
                Label: e.Title,
                Value: e.Date.ToString("yyyy-MM-dd"),
                Tone:  e.ImpactScore.HasValue && e.ImpactScore.Value < 0 ? "down" : "up",
                Href:  $"/events/{e.Id}/summary"
            ));

        var countries = skip == "countries" ? Enumerable.Empty<TickerItem>() : _countries.GetAll()
            .Where(c => c.GdpUsd.HasValue)
            .OrderByDescending(c => c.GdpUsd)
            .Take(8)
            .Select(c => new TickerItem(
                Kind:  "CTRY",
                Label: $"{c.Code} {c.Name}",
                Value: $"GDP ${c.GdpUsd!.Value / 1_000_000_000:N1}B",
                Tone:  "up",
                Href:  $"/countries/{c.Id}/overview"
            ));

        var companies = skip == "companies" ? Enumerable.Empty<TickerItem>() : _companies.GetAll()
            .Where(c => c.RevenueTotal.HasValue)
            .OrderByDescending(c => c.RevenueTotal)
            .Take(8)
            .Select(c => new TickerItem(
                Kind:  "CO",
                Label: c.Name,
                Value: $"REV ${c.RevenueTotal!.Value / 1_000_000_000:N1}B",
                Tone:  c.GrossMargin.HasValue && c.GrossMargin.Value < 0 ? "down" : "up",
                Href:  $"/companies/{c.Id}/profile"
            ));

        var merged = events.Concat(countries).Concat(companies).ToList();

        var rng = new Random(DateTime.UtcNow.Hour);
        var shuffled = merged.OrderBy(_ => rng.Next()).ToList();

        return Json(shuffled);
    }

    private record TickerItem(string Kind, string Label, string Value, string Tone, string Href);
}
