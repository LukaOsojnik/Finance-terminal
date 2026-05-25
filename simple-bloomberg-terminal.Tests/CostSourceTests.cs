using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>CostSourcesController</c> (plain uniform CRUD). No CostSource is
/// seeded, so the ok-path GetById/Update/Delete tests create their own row first via the API.
/// </summary>
public class CostSourceTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    private async Task<long> CreateCostSourceAsync(string name)
    {
        var resp = await Client.PostAsJsonAsync("/api/costsources", new
        {
            costBase = CostBase.COGS,
            name,
            companyId = AppleId,
            value = 200_000_000_000d,
            dataSource = DataSource.MANUAL
        });
        resp.EnsureSuccessStatusCode();
        var dto = await resp.Content.ReadFromJsonAsync<CostSourceDto>();
        return dto!.Id;
    }

    [Fact]
    public async Task GetAll_Empty_Returns200()
    {
        var items = await Client.GetFromJsonAsync<List<CostSourceDto>>("/api/costsources");

        Assert.NotNull(items);
        Assert.Empty(items!); // none seeded
    }

    [Fact]
    public async Task GetById_Existing_ReturnsSource()
    {
        var id = await CreateCostSourceAsync("Manufacturing");

        var resp = await Client.GetAsync($"/api/costsources/{id}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CostSourceDto>();
        Assert.Equal("Manufacturing", dto!.Name);
        Assert.Equal(AppleId, dto.CompanyId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/costsources/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { costBase = CostBase.OPEX, name = "R&D", companyId = AppleId, value = 30_000_000_000d };

        var resp = await Client.PostAsJsonAsync("/api/costsources", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CostSourceDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("R&D", dto.Name);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { costBase = CostBase.OPEX, value = 1_000d }; // missing Name + CompanyId

        var resp = await Client.PostAsJsonAsync("/api/costsources", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedFields()
    {
        var id = await CreateCostSourceAsync("Logistics");

        var body = new { costBase = CostBase.TOTAL_COSTS, name = "Logistics & Freight", companyId = AppleId };

        var resp = await Client.PutAsJsonAsync($"/api/costsources/{id}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CostSourceDto>();
        Assert.Equal("Logistics & Freight", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { costBase = CostBase.OPEX, name = "Ghost", companyId = AppleId };

        var resp = await Client.PutAsJsonAsync($"/api/costsources/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var id = await CreateCostSourceAsync("Disposable");

        var resp = await Client.DeleteAsync($"/api/costsources/{id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/costsources/{id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/costsources/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
