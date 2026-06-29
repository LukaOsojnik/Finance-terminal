using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Services.Search;

public interface ISearchService
{
    // Fan a single query across Companies, Countries and Events, returning at most
    // `perGroup` hits per type. Empty/blank query returns no groups.
    IReadOnlyList<SearchGroup> Search(string? query, int perGroup = 5);
}
