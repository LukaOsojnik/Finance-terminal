using AutoMapper;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal;

/// <summary>
/// Builds the hub-and-spoke graph (nodes + edges) from a Company loaded with its graph
/// relations. Registered as the AutoMapper Company -> GraphResponse converter, so both the
/// MVC GraphController (renders vis-network) and the API GraphController (returns JSON) get
/// the identical graph via _mapper.Map&lt;GraphResponse&gt;(company). Pure transform: no DB
/// access. Caller must load the company via ICompanyRepository.GetWithGraphRelations*.
/// </summary>
public class CompanyGraphConverter : ITypeConverter<Company, GraphResponse>
{
    public GraphResponse Convert(Company company, GraphResponse destination, ResolutionContext context)
    {
        var nodes = new List<GraphNode>();
        var edges = new List<GraphEdge>();

        // A source connects to the EDGAR filings that prove it — proof is per field, so a source
        // may cite several filings via its reviews. These are no longer drawn as graph nodes;
        // instead each source's distinct proof filings are collected here and carried on its leaf
        // node, so the click popup can list them without cluttering the canvas. Dedup per source.
        static IReadOnlyList<GraphFiling> CollectFilings(IEnumerable<SourceFieldReview> reviews)
        {
            var seen = new HashSet<long>();
            var filings = new List<GraphFiling>();
            foreach (var f in reviews
                         .Where(rv => rv.DeletedAt == null && rv.Filing != null && rv.Filing.DeletedAt == null)
                         .Select(rv => rv.Filing!))
            {
                if (!seen.Add(f.Id)) continue;
                var date = f.FilingDate?.ToString("yyyy-MM-dd");
                filings.Add(new GraphFiling(
                    Label: f.Form ?? "Filing",
                    Detail: $"{f.AccessionNumber}{(date is null ? "" : " · " + date)}"
                ));
            }
            return filings;
        }

        var centerId = $"company:{company.Id}";
        nodes.Add(new GraphNode(
            Id: centerId,
            Label: company.Name,
            Group: "center",
            Title: $"{company.Sector} · {company.Country?.Code}",
            ValueUsd: company.RevenueTotal
        ));

        // Hub-and-spoke: center → category hub → leaves. Hubs only added when children exist.
        // Approved only — Pending user contributions stay off the public graph until reviewed.
        var revenues = company.RevenueSources.Where(x => x.DeletedAt == null && x.Status == ContributionStatus.Approved).ToList();
        var costs    = company.CostSources.Where(x => x.DeletedAt == null && x.Status == ContributionStatus.Approved).ToList();
        var events   = company.Events.Where(x => x.DeletedAt == null).ToList();

        if (revenues.Count > 0)
        {
            var hubId = $"hub:rev:{company.Id}";
            nodes.Add(new GraphNode(hubId, "REVENUE SOURCES", "hub-revenue", $"{revenues.Count} items", revenues.Sum(x => x.Value ?? 0)));
            edges.Add(new GraphEdge(centerId, hubId, $"{revenues.Count}", "revenue"));
            foreach (var r in revenues)
            {
                var nodeId = $"rev:{r.Id}";
                var linked = r.RelatedCompany != null && r.RelatedCompany.DeletedAt == null;
                var navId = linked ? (long?)r.RelatedCompanyId : null;
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: linked ? r.RelatedCompany!.Name : r.Name,
                    Group: "revenue",
                    Title: $"{r.SourceType} · ${(r.Value ?? 0) / 1e9:F2}B",
                    ValueUsd: r.Value,
                    RelatedCompanyId: navId,
                    Filings: CollectFilings(r.Reviews),
                    MarketCapUsd: linked ? r.RelatedCompany!.MarketCap : null
                ));
                edges.Add(new GraphEdge(hubId, nodeId, r.Value.HasValue ? $"${r.Value.Value / 1e9:F1}B" : null, "revenue"));
            }
        }

        if (costs.Count > 0)
        {
            var hubId = $"hub:cost:{company.Id}";
            nodes.Add(new GraphNode(hubId, "COST SOURCES", "hub-cost", $"{costs.Count} items", costs.Sum(x => x.Value ?? 0)));
            edges.Add(new GraphEdge(centerId, hubId, $"{costs.Count}", "cost"));
            foreach (var c in costs)
            {
                var nodeId = $"cost:{c.Id}";
                var linked = c.RelatedCompany != null && c.RelatedCompany.DeletedAt == null;
                var navId = linked ? (long?)c.RelatedCompanyId : null;
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: linked ? c.RelatedCompany!.Name : c.Name,
                    Group: "cost",
                    Title: $"{c.CostBase} · ${(c.Value ?? 0) / 1e9:F2}B",
                    ValueUsd: c.Value,
                    RelatedCompanyId: navId,
                    Filings: CollectFilings(c.Reviews),
                    MarketCapUsd: linked ? c.RelatedCompany!.MarketCap : null
                ));
                edges.Add(new GraphEdge(hubId, nodeId, c.Value.HasValue ? $"${c.Value.Value / 1e9:F1}B" : null, "cost"));
            }
        }

        if (events.Count > 0)
        {
            var hubId = $"hub:event:{company.Id}";
            nodes.Add(new GraphNode(hubId, "EVENTS", "hub-event", $"{events.Count} items", null));
            edges.Add(new GraphEdge(centerId, hubId, $"{events.Count}", "event"));
            foreach (var e in events)
            {
                var nodeId = $"event:{e.Id}";
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: e.Title,
                    Group: "event",
                    Title: $"{e.Type} · {e.Date:yyyy-MM-dd}",
                    ValueUsd: null
                ));
                edges.Add(new GraphEdge(hubId, nodeId, null, "event"));
            }
        }

        // No separate RELATED COMPANIES hub: linked counterparties are reachable through the
        // revenue/cost leaf nodes (each carries RelatedCompanyId for navigation), and the reciprocal
        // source rows give each company its own leaf pointing back — so the relationship is already
        // represented from both ends without a dedicated hub.

        return new GraphResponse(
            CenterId: company.Id,
            CenterLabel: company.Name,
            Nodes: nodes,
            Edges: edges
        );
    }
}
