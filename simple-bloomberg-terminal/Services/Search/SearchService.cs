using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services.Search;

// Cross-entity search. Reuses each repository's existing prefix-LIKE Search(term)
// — the dataset is small enough that no full-text index is warranted (the repos
// already normalise names in memory elsewhere). Results are grouped by type in a
// fixed priority order: Companies first (primary lookup), then Countries, Events.
public class SearchService : ISearchService
{
    private readonly ICompanyRepository _companies;
    private readonly ICountryRepository _countries;
    private readonly IEventRepository _events;

    public SearchService(
        ICompanyRepository companies,
        ICountryRepository countries,
        IEventRepository events)
    {
        _companies = companies;
        _countries = countries;
        _events = events;
    }

    public IReadOnlyList<SearchGroup> Search(string? query, int perGroup = 5)
    {
        if (string.IsNullOrWhiteSpace(query)) return [];

        var groups = new List<SearchGroup>();

        var companies = _companies.Search(query).Take(perGroup).Select(c => new SearchHit(
            Kind:     "CO",
            Label:    c.Name,
            Sublabel: Join(c.Sector?.ToString(), c.Country?.Name),
            Href:     $"/companies/{c.Id}/profile"
        )).ToList();
        if (companies.Count > 0) groups.Add(new SearchGroup("CO", "Companies", companies));

        var countries = _countries.Search(query).Take(perGroup).Select(c => new SearchHit(
            Kind:     "CTRY",
            Label:    $"{c.Code} · {c.Name}",
            Sublabel: c.Region,
            Href:     $"/countries/{c.Id}/overview"
        )).ToList();
        if (countries.Count > 0) groups.Add(new SearchGroup("CTRY", "Countries", countries));

        var events = _events.Search(query).Take(perGroup).Select(e => new SearchHit(
            Kind:     "EVT",
            Label:    e.Title,
            Sublabel: Join(e.Type.ToString(), e.Date.ToString("yyyy-MM-dd")),
            Href:     $"/events/{e.Id}/summary"
        )).ToList();
        if (events.Count > 0) groups.Add(new SearchGroup("EVT", "Events", events));

        return groups;
    }

    // Join non-empty parts with a middle dot; null if nothing survives.
    private static string? Join(params string?[] parts)
    {
        var kept = parts.Where(p => !string.IsNullOrWhiteSpace(p));
        var joined = string.Join(" · ", kept);
        return joined.Length == 0 ? null : joined;
    }
}
