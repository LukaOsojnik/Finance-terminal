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
    long? RelatedCompanyId = null
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
