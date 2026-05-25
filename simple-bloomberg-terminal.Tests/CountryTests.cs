using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>CountriesController</c> (plain uniform CRUD).</summary>
public class CountryTests : ApiTestBase
{
    private const long UsId = CustomWebApplicationFactory.CountryUsId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeededCountries()
    {
        var items = await Client.GetFromJsonAsync<List<CountryDto>>("/api/countries");

        Assert.NotNull(items);
        Assert.Contains(items!, c => c.Code == "US");
        Assert.Contains(items!, c => c.Code == "DE");
    }

    [Fact]
    public async Task GetAll_WithQuery_FiltersWithLike()
    {
        var items = await Client.GetFromJsonAsync<List<CountryDto>>("/api/countries?q=United");

        Assert.NotNull(items);
        Assert.Contains(items!, c => c.Code == "US");
        Assert.DoesNotContain(items!, c => c.Code == "DE");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsCountry()
    {
        var resp = await Client.GetAsync($"/api/countries/{UsId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDto>();
        Assert.Equal("United States", dto!.Name);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/countries/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { code = "FR", name = "France", region = "Europe", currencyCode = "EUR" };

        var resp = await Client.PostAsJsonAsync("/api/countries", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("France", dto.Name);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { region = "Europe" }; // missing Code, Name, CurrencyCode

        var resp = await Client.PostAsJsonAsync("/api/countries", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedFields()
    {
        var body = new { code = "US", name = "USA", region = "Americas", currencyCode = "USD" };

        var resp = await Client.PutAsJsonAsync($"/api/countries/{UsId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryDto>();
        Assert.Equal("USA", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { code = "XX", name = "Nowhere", region = "Nowhere", currencyCode = "XXX" };

        var resp = await Client.PutAsJsonAsync($"/api/countries/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        // Create a standalone country (no companies/events) so the soft-delete is unobstructed.
        var created = await Client.PostAsJsonAsync("/api/countries",
            new { code = "ES", name = "Spain", region = "Europe", currencyCode = "EUR" });
        var dto = await created.Content.ReadFromJsonAsync<CountryDto>();

        var resp = await Client.DeleteAsync($"/api/countries/{dto!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/countries/{dto.Id}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/countries/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
