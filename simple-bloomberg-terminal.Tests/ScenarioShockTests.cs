using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>ScenarioShocksController</c> (plain uniform CRUD).</summary>
public class ScenarioShockTests : ApiTestBase
{
    private const long ShockId = CustomWebApplicationFactory.ScenarioShockId;
    private const long ScenarioId = CustomWebApplicationFactory.ScenarioId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<ScenarioShockDto>>("/api/scenarioshocks");

        Assert.NotNull(items);
        Assert.Contains(items!, s => s.ScenarioId == ScenarioId && s.Factor == CostFactor.CAPITAL);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsShock()
    {
        var resp = await Client.GetAsync($"/api/scenarioshocks/{ShockId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioShockDto>();
        Assert.Equal(ScenarioId, dto!.ScenarioId);
        Assert.Equal(ImpactKind.Cost, dto.Kind);
        Assert.Equal(ShockTarget.FACTOR, dto.Target);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/scenarioshocks/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { scenarioId = ScenarioId, kind = ImpactKind.Cost, target = ShockTarget.FACTOR, factor = CostFactor.ENERGY, magnitude = 0.2 };

        var resp = await Client.PostAsJsonAsync("/api/scenarioshocks", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioShockDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal(CostFactor.ENERGY, dto.Factor);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { magnitude = 0.2 }; // missing ScenarioId, Kind, Target

        var resp = await Client.PostAsJsonAsync("/api/scenarioshocks", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedMagnitude()
    {
        var body = new { scenarioId = ScenarioId, kind = ImpactKind.Cost, target = ShockTarget.FACTOR, factor = CostFactor.CAPITAL, magnitude = 0.25 };

        var resp = await Client.PutAsJsonAsync($"/api/scenarioshocks/{ShockId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<ScenarioShockDto>();
        Assert.Equal(0.25, dto!.Magnitude);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { scenarioId = ScenarioId, kind = ImpactKind.Cost, target = ShockTarget.FACTOR, factor = CostFactor.CAPITAL, magnitude = 0.25 };

        var resp = await Client.PutAsJsonAsync($"/api/scenarioshocks/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/scenarioshocks/{ShockId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/scenarioshocks/{ShockId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/scenarioshocks/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
