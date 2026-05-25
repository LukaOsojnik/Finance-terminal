using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>GdpSnapshotsController</c> (uniform CRUD). Adds a
/// <c>[Range(1800,2200)]</c> validation case on <c>Year</c> beyond the missing-Required check.
/// </summary>
public class GdpSnapshotTests : ApiTestBase
{
    private const long SnapshotId = CustomWebApplicationFactory.GdpSnapshotId; // US, 2023
    private const long UsId = CustomWebApplicationFactory.CountryUsId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<GdpSnapshotDto>>("/api/gdpsnapshots");

        Assert.NotNull(items);
        Assert.Contains(items!, g => g.Year == 2023 && g.CountryId == UsId);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsSnapshot()
    {
        var resp = await Client.GetAsync($"/api/gdpsnapshots/{SnapshotId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<GdpSnapshotDto>();
        Assert.Equal(2023, dto!.Year);
        Assert.Equal(UsId, dto.CountryId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/gdpsnapshots/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { countryId = UsId, year = 2024, gdpUsd = 26_000_000_000_000d };

        var resp = await Client.PostAsJsonAsync("/api/gdpsnapshots", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<GdpSnapshotDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal(2024, dto.Year);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { year = 2024, gdpUsd = 1_000d }; // missing CountryId

        var resp = await Client.PostAsJsonAsync("/api/gdpsnapshots", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_YearOutOfRange_Returns400()
    {
        var body = new { countryId = UsId, year = 1000, gdpUsd = 1_000d }; // Year < 1800

        var resp = await Client.PostAsJsonAsync("/api/gdpsnapshots", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedValue()
    {
        var body = new { countryId = UsId, year = 2023, gdpUsd = 24_500_000_000_000d };

        var resp = await Client.PutAsJsonAsync($"/api/gdpsnapshots/{SnapshotId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<GdpSnapshotDto>();
        Assert.Equal(24_500_000_000_000d, dto!.GdpUsd);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { countryId = UsId, year = 2023, gdpUsd = 1_000d };

        var resp = await Client.PutAsJsonAsync($"/api/gdpsnapshots/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/gdpsnapshots/{SnapshotId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/gdpsnapshots/{SnapshotId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/gdpsnapshots/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
