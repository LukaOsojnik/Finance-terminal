using AutoMapper;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal;

internal record RelatedSlot(Company Co, double InflowUsd, double OutflowUsd);

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
        // may cite several filings via its reviews. Dedup the filing node globally; dedup edges
        // per source (multiple fields can cite the same filing).
        var filingSeen = new HashSet<long>();
        void AddFilingLinks(IEnumerable<SourceFieldReview> reviews, string sourceNodeId)
        {
            var linked = new HashSet<long>();
            foreach (var f in reviews
                         .Where(rv => rv.DeletedAt == null && rv.Filing != null && rv.Filing.DeletedAt == null)
                         .Select(rv => rv.Filing!))
            {
                if (!linked.Add(f.Id)) continue;   // already drew this source's edge to f

                var filingId = $"filing:{f.Id}";
                if (filingSeen.Add(f.Id))
                {
                    var date = f.FilingDate?.ToString("yyyy-MM-dd");
                    nodes.Add(new GraphNode(
                        Id: filingId,
                        Label: f.Form ?? "Filing",
                        Group: "filing",
                        Title: $"{f.AccessionNumber}{(date is null ? "" : " · " + date)}",
                        ValueUsd: null
                    ));
                }
                edges.Add(new GraphEdge(sourceNodeId, filingId, "proof", "filing"));
            }
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
        var revenues = company.RevenueSources.Where(x => x.DeletedAt == null).ToList();
        var costs    = company.CostSources.Where(x => x.DeletedAt == null).ToList();
        var events   = company.Events.Where(x => x.DeletedAt == null).ToList();

        if (revenues.Count > 0)
        {
            var hubId = $"hub:rev:{company.Id}";
            nodes.Add(new GraphNode(hubId, "REVENUE SOURCES", "hub-revenue", $"{revenues.Count} items", revenues.Sum(x => x.Value ?? 0)));
            edges.Add(new GraphEdge(centerId, hubId, $"{revenues.Count}", "revenue"));
            foreach (var r in revenues)
            {
                var nodeId = $"rev:{r.Id}";
                var navId = (r.RelatedCompany != null && r.RelatedCompany.DeletedAt == null) ? (long?)r.RelatedCompanyId : null;
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: r.Name,
                    Group: "revenue",
                    Title: $"{r.SourceType} · ${(r.Value ?? 0) / 1e9:F2}B",
                    ValueUsd: r.Value,
                    RelatedCompanyId: navId
                ));
                edges.Add(new GraphEdge(hubId, nodeId, r.Value.HasValue ? $"${r.Value.Value / 1e9:F1}B" : null, "revenue"));
                AddFilingLinks(r.Reviews, nodeId);
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
                var navId = (c.RelatedCompany != null && c.RelatedCompany.DeletedAt == null) ? (long?)c.RelatedCompanyId : null;
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: c.Name,
                    Group: "cost",
                    Title: $"{c.CostBase} · ${(c.Value ?? 0) / 1e9:F2}B",
                    ValueUsd: c.Value,
                    RelatedCompanyId: navId
                ));
                edges.Add(new GraphEdge(hubId, nodeId, c.Value.HasValue ? $"${c.Value.Value / 1e9:F1}B" : null, "cost"));
                AddFilingLinks(c.Reviews, nodeId);
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

        // Related-company supply graph: dedup across revenue + cost directions.
        // A counterparty may both buy from us (RevenueSources.RelatedCompanyId) and supply us (CostSources.RelatedCompanyId).
        var related = new Dictionary<long, RelatedSlot>();

        foreach (var r in company.RevenueSources.Where(x => x.DeletedAt == null && x.RelatedCompany != null && x.RelatedCompany.DeletedAt == null))
        {
            var rc = r.RelatedCompany!;
            var slot = related.GetValueOrDefault(rc.Id, new RelatedSlot(rc, 0d, 0d));
            related[rc.Id] = slot with { InflowUsd = slot.InflowUsd + (r.Value ?? 0) };
        }
        foreach (var c in company.CostSources.Where(x => x.DeletedAt == null && x.RelatedCompany != null && x.RelatedCompany.DeletedAt == null))
        {
            var rc = c.RelatedCompany!;
            var slot = related.GetValueOrDefault(rc.Id, new RelatedSlot(rc, 0d, 0d));
            related[rc.Id] = slot with { OutflowUsd = slot.OutflowUsd + (c.Value ?? 0) };
        }
        // Incoming-direction: companies that list THIS company as their related counterparty.
        foreach (var r in company.RevenueFromDependents.Where(x => x.DeletedAt == null && x.Company != null && x.Company.DeletedAt == null))
        {
            var rc = r.Company!;
            var slot = related.GetValueOrDefault(rc.Id, new RelatedSlot(rc, 0d, 0d));
            related[rc.Id] = slot with { OutflowUsd = slot.OutflowUsd + (r.Value ?? 0) };
        }
        foreach (var c in company.CostFromDependents.Where(x => x.DeletedAt == null && x.Company != null && x.Company.DeletedAt == null))
        {
            var rc = c.Company!;
            var slot = related.GetValueOrDefault(rc.Id, new RelatedSlot(rc, 0d, 0d));
            related[rc.Id] = slot with { InflowUsd = slot.InflowUsd + (c.Value ?? 0) };
        }

        if (related.Count > 0)
        {
            var hubId = $"hub:related:{company.Id}";
            nodes.Add(new GraphNode(hubId, "RELATED COMPANIES", "hub-related", $"{related.Count} counterparties", null));
            edges.Add(new GraphEdge(centerId, hubId, $"{related.Count}", "related"));
            foreach (var (otherId, slot) in related)
            {
                var nodeId = $"company:{otherId}";
                string tag = (slot.InflowUsd > 0, slot.OutflowUsd > 0) switch
                {
                    (true, true)  => "customer + supplier",
                    (true, false) => "customer",
                    (false, true) => "supplier",
                    _             => "linked"
                };
                nodes.Add(new GraphNode(
                    Id: nodeId,
                    Label: slot.Co.Name,
                    Group: "related",
                    Title: $"{tag} · in ${slot.InflowUsd / 1e9:F2}B / out ${slot.OutflowUsd / 1e9:F2}B",
                    ValueUsd: slot.InflowUsd + slot.OutflowUsd
                ));
                edges.Add(new GraphEdge(hubId, nodeId, tag, "related"));
            }
        }

        return new GraphResponse(
            CenterId: company.Id,
            CenterLabel: company.Name,
            Nodes: nodes,
            Edges: edges
        );
    }
}
