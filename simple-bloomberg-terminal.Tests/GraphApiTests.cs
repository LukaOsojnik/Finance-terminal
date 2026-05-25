using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for the API <c>GraphController</c> (<c>GET /api/graph/company?cik=</c>)
/// exercised through the real HTTP pipeline. The endpoint looks a company up by exact,
/// literal CIK and returns the hub-and-spoke <see cref="GraphResponse"/> built by the shared
/// <c>Company -> GraphResponse</c> AutoMapper converter. Seed: Apple (CIK 0000320193, one
/// Event, no sources) and Microsoft (CIK 0000789019, one "Cloud" RevenueSource).
/// </summary>
public class GraphApiTests : ApiTestBase
{
    private const string AppleCik = "0000320193";      // CompanyDeletableId, has an Event
    private const string MicrosoftCik = "0000789019";  // CompanyBlockedId, has a "Cloud" RevenueSource

    [Fact]
    public async Task ExistingCik_ReturnsGraphCenteredOnCompany()
    {
        var resp = await Client.GetAsync($"/api/graph/company?cik={AppleCik}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var graph = await resp.Content.ReadFromJsonAsync<GraphResponse>();
        Assert.NotNull(graph);
        Assert.Equal(CustomWebApplicationFactory.CompanyDeletableId, graph!.CenterId);
        Assert.Equal("Apple", graph.CenterLabel);
        Assert.Contains(graph.Nodes, n => n.Group == "center" && n.Label == "Apple");
    }

    [Fact]
    public async Task ExistingCik_ProjectsChildRelationsAsHubAndLeaf()
    {
        // Microsoft's seeded "Cloud" RevenueSource => a revenue hub plus its leaf node.
        var graph = await Client.GetFromJsonAsync<GraphResponse>($"/api/graph/company?cik={MicrosoftCik}");

        Assert.NotNull(graph);
        Assert.Contains(graph!.Nodes, n => n.Group == "hub-revenue");
        Assert.Contains(graph.Nodes, n => n.Group == "revenue" && n.Label == "Cloud");
    }

    [Fact]
    public async Task UnknownCik_Returns404()
    {
        var resp = await Client.GetAsync("/api/graph/company?cik=9999999999");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task NonPaddedCik_Returns404_BecauseMatchIsLiteral()
    {
        // "320193" is Apple's CIK without the SEC zero-padding. Lookup is exact/literal,
        // so the unpadded form must NOT resolve to the padded seed row.
        var resp = await Client.GetAsync("/api/graph/company?cik=320193");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task BlankCik_Returns400()
    {
        var resp = await Client.GetAsync("/api/graph/company?cik=");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }
}
