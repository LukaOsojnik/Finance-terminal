using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>CountryChallengesController</c> (plain uniform CRUD).</summary>
public class CountryChallengeTests : ApiTestBase
{
    private const long ChallengeId = CustomWebApplicationFactory.CountryChallengeId; // on US
    private const long UsId = CustomWebApplicationFactory.CountryUsId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<CountryChallengeDto>>("/api/countrychallenges");

        Assert.NotNull(items);
        Assert.Contains(items!, c => c.Text == "High public debt");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsChallenge()
    {
        var resp = await Client.GetAsync($"/api/countrychallenges/{ChallengeId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryChallengeDto>();
        Assert.Equal("High public debt", dto!.Text);
        Assert.Equal(UsId, dto.CountryId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/countrychallenges/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { countryId = UsId, text = "Aging infrastructure" };

        var resp = await Client.PostAsJsonAsync("/api/countrychallenges", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CountryChallengeDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Aging infrastructure", dto.Text);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { countryId = UsId }; // missing Text

        var resp = await Client.PostAsJsonAsync("/api/countrychallenges", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedText()
    {
        var body = new { countryId = UsId, text = "High and rising public debt" };

        var resp = await Client.PutAsJsonAsync($"/api/countrychallenges/{ChallengeId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CountryChallengeDto>();
        Assert.Equal("High and rising public debt", dto!.Text);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { countryId = UsId, text = "Nope" };

        var resp = await Client.PutAsJsonAsync($"/api/countrychallenges/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/countrychallenges/{ChallengeId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/countrychallenges/{ChallengeId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/countrychallenges/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
