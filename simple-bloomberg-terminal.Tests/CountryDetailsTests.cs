using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>CountryDetailsController</c>. Quirk: this entity is keyed 1:1 by
/// <c>CountryId</c> (it has no identity of its own), so every route segment is a country id.
/// Seeded details exist for the US (id 1); Germany (id 2) has none -> used for the Create case.
/// </summary>
public class CountryDetailsTests : ApiTestBase
{
    private const long UsId = CustomWebApplicationFactory.CountryUsId;   // has details
    private const long DeId = CustomWebApplicationFactory.CountryDeId;   // no details yet
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeededDetails()
    {
        var items = await Client.GetFromJsonAsync<List<CountryDetailsDto>>("/api/countrydetails");

        Assert.NotNull(items);
        Assert.Contains(items!, d => d.CountryId == UsId && d.MarketPosition == "Global leader");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsDetails()
    {
        var resp = await Client.GetAsync($"/api/countrydetails/{UsId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDetailsDto>();
        Assert.Equal(UsId, dto!.CountryId);
        Assert.Equal("Global leader", dto.MarketPosition);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/countrydetails/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_KeyedByCountryId()
    {
        var body = new { countryId = DeId, marketPosition = "Industrial powerhouse" };

        var resp = await Client.PostAsJsonAsync("/api/countrydetails", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDetailsDto>();
        Assert.Equal(DeId, dto!.CountryId);
        Assert.Equal("Industrial powerhouse", dto.MarketPosition);

        var followUp = await Client.GetAsync($"/api/countrydetails/{DeId}");
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { countryId = DeId }; // missing MarketPosition

        var resp = await Client.PostAsJsonAsync("/api/countrydetails", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedField()
    {
        var body = new { countryId = UsId, marketPosition = "Dominant" };

        var resp = await Client.PutAsJsonAsync($"/api/countrydetails/{UsId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDetailsDto>();
        Assert.Equal("Dominant", dto!.MarketPosition);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { countryId = MissingId, marketPosition = "Nowhere" };

        var resp = await Client.PutAsJsonAsync($"/api/countrydetails/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/countrydetails/{UsId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/countrydetails/{UsId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/countrydetails/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
