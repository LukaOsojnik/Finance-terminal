using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>CountryAdvantagesController</c> (plain uniform CRUD).</summary>
public class CountryAdvantageTests : ApiTestBase
{
    private const long AdvantageId = CustomWebApplicationFactory.CountryAdvantageId; // on US
    private const long UsId = CustomWebApplicationFactory.CountryUsId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<CountryAdvantageDto>>("/api/countryadvantages");

        Assert.NotNull(items);
        Assert.Contains(items!, a => a.Text == "Deep capital markets");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsAdvantage()
    {
        var resp = await Client.GetAsync($"/api/countryadvantages/{AdvantageId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryAdvantageDto>();
        Assert.Equal("Deep capital markets", dto!.Text);
        Assert.Equal(UsId, dto.CountryId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/countryadvantages/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { countryId = UsId, text = "Skilled workforce" };

        var resp = await Client.PostAsJsonAsync("/api/countryadvantages", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CountryAdvantageDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Skilled workforce", dto.Text);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { countryId = UsId }; // missing Text

        var resp = await Client.PostAsJsonAsync("/api/countryadvantages", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedText()
    {
        var body = new { countryId = UsId, text = "Deep, liquid capital markets" };

        var resp = await Client.PutAsJsonAsync($"/api/countryadvantages/{AdvantageId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryAdvantageDto>();
        Assert.Equal("Deep, liquid capital markets", dto!.Text);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { countryId = UsId, text = "Nope" };

        var resp = await Client.PutAsJsonAsync($"/api/countryadvantages/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/countryadvantages/{AdvantageId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/countryadvantages/{AdvantageId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/countryadvantages/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
