using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class GraphIndexViewModel
{
    public IEnumerable<Company> Companies { get; set; } = [];
    public long? SelectedCompanyId { get; set; }
}

public record GraphNode(
    string Id,
    string Label,
    string Group,
    string? Title,
    double? ValueUsd,
    long? RelatedCompanyId = null,
    IReadOnlyList<GraphFiling>? Filings = null,
    double? MarketCapUsd = null
);

// Proof filings a source cites — carried on the leaf node (not drawn as graph nodes) so the
// click popup can list them without cluttering the canvas.
public record GraphFiling(
    string Label,
    string? Detail
);

public record GraphEdge(
    string From,
    string To,
    string? Label,
    string Group
);

public record GraphResponse(
    long CenterId,
    string CenterLabel,
    IEnumerable<GraphNode> Nodes,
    IEnumerable<GraphEdge> Edges
);
