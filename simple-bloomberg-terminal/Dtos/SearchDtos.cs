namespace simple_bloomberg_terminal.Dtos;

// One row in the global search dropdown. Mirrors the ticker's TickerItem shape
// (a Kind tag + a deep-link Href) so both features share one render path on the
// front end. Sublabel is the secondary, dimmed line (sector, region, date…).
public record SearchHit(string Kind, string Label, string? Sublabel, string Href);

// Results bucketed by entity type. Grouping sidesteps cross-type relevance
// ranking: each type is its own section, shown in a fixed priority order.
public record SearchGroup(string Kind, string Title, IReadOnlyList<SearchHit> Hits);
