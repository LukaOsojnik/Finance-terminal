using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>ScenariosController</c> (CRUD; GET by id projects nested shocks).</summary>
public class ScenarioTests : ApiTestBase
{
    private const long ScenarioId = CustomWebApplicationFactory.ScenarioId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<ScenarioDto>>("/api/scenarios");

        Assert.NotNull(items);
        Assert.Contains(items!, s => s.Name == "Rate hike");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsScenario_WithShock()
    {
        var resp = await Client.GetAsync($"/api/scenarios/{ScenarioId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioDto>();
        Assert.Equal("Rate hike", dto!.Name);
        Assert.Single(dto.Shocks);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/scenarios/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { name = "Oil shock", description = "War-driven supply cut" };

        var resp = await Client.PostAsJsonAsync("/api/scenarios", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Oil shock", dto.Name);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { description = "no name" }; // missing Name

        var resp = await Client.PostAsJsonAsync("/api/scenarios", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedName()
    {
        var body = new { name = "Rate hike v2", description = "Capital cost push" };

        var resp = await Client.PutAsJsonAsync($"/api/scenarios/{ScenarioId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioDto>();
        Assert.Equal("Rate hike v2", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { name = "Nope" };

        var resp = await Client.PutAsJsonAsync($"/api/scenarios/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/scenarios/{ScenarioId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/scenarios/{ScenarioId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/scenarios/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
