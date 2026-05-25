using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>RevenueSourcesController</c> (plain uniform CRUD).</summary>
public class RevenueSourceTests : ApiTestBase
{
    private const long RevenueSourceId = CustomWebApplicationFactory.RevenueSourceId; // "Cloud" on Microsoft
    private const long MicrosoftId = CustomWebApplicationFactory.CompanyBlockedId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<RevenueSourceDto>>("/api/revenuesources");

        Assert.NotNull(items);
        Assert.Contains(items!, r => r.Name == "Cloud");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsSource()
    {
        var resp = await Client.GetAsync($"/api/revenuesources/{RevenueSourceId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<RevenueSourceDto>();
        Assert.Equal("Cloud", dto!.Name);
        Assert.Equal(MicrosoftId, dto.CompanyId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/revenuesources/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new
        {
            sourceType = SourceType.PRODUCT,
            name = "Devices",
            companyId = MicrosoftId,
            value = 50_000_000_000d,
            dataSource = DataSource.MANUAL
        };

        var resp = await Client.PostAsJsonAsync("/api/revenuesources", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<RevenueSourceDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Devices", dto.Name);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { sourceType = SourceType.PRODUCT, value = 1_000d }; // missing Name + CompanyId

        var resp = await Client.PostAsJsonAsync("/api/revenuesources", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedFields()
    {
        var body = new { sourceType = SourceType.SEGMENT, name = "Cloud & AI", companyId = MicrosoftId };

        var resp = await Client.PutAsJsonAsync($"/api/revenuesources/{RevenueSourceId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<RevenueSourceDto>();
        Assert.Equal("Cloud & AI", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { sourceType = SourceType.SEGMENT, name = "Ghost", companyId = MicrosoftId };

        var resp = await Client.PutAsJsonAsync($"/api/revenuesources/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/revenuesources/{RevenueSourceId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/revenuesources/{RevenueSourceId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/revenuesources/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
