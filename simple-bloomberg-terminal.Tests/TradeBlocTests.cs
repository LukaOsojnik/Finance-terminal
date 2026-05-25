using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>TradeBlocsController</c>. Quirks: member countries are applied
/// from an id list on POST/PUT, and delete is blocked (409) while member countries exist.
/// </summary>
public class TradeBlocTests : ApiTestBase
{
    private const long DeletableId = CustomWebApplicationFactory.TradeBlocDeletableId; // EU, no members
    private const long BlockedId = CustomWebApplicationFactory.TradeBlocBlockedId;     // NAFTA, member US
    private const long MissingId = CustomWebApplicationFactory.MissingId;
    private const long CountryUsId = CustomWebApplicationFactory.CountryUsId;

    [Fact]
    public async Task GetAll_ReturnsSeededBlocs()
    {
        var items = await Client.GetFromJsonAsync<List<TradeBlocDto>>("/api/tradeblocs");

        Assert.NotNull(items);
        Assert.Contains(items!, b => b.Code == "EU");
        Assert.Contains(items!, b => b.Code == "NAFTA");
    }

    [Fact]
    public async Task GetAll_WithQuery_FiltersWithLike()
    {
        var items = await Client.GetFromJsonAsync<List<TradeBlocDto>>("/api/tradeblocs?q=European");

        Assert.NotNull(items);
        Assert.Contains(items!, b => b.Code == "EU");
        Assert.DoesNotContain(items!, b => b.Code == "NAFTA");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsNestedMembers()
    {
        var resp = await Client.GetAsync($"/api/tradeblocs/{BlockedId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<TradeBlocDto>();
        Assert.Equal("NAFTA", dto!.Code);
        Assert.Contains(dto.Countries, c => c.Name == "United States");
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/tradeblocs/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_AndAppliesMembers()
    {
        var body = new
        {
            name = "Mercosur",
            code = "MSUR",
            countryIds = new[] { CountryUsId }
        };

        var resp = await Client.PostAsJsonAsync("/api/tradeblocs", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await Client.GetFromJsonAsync<TradeBlocDto>(resp.Headers.Location!.ToString());
        Assert.Equal("Mercosur", dto!.Name);
        Assert.Contains(dto.Countries, c => c.Name == "United States");
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { name = "No Code Bloc" }; // missing Code

        var resp = await Client.PostAsJsonAsync("/api/tradeblocs", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedFields()
    {
        var body = new { name = "European Union (rev.)", code = "EU" };

        var resp = await Client.PutAsJsonAsync($"/api/tradeblocs/{DeletableId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<TradeBlocDto>();
        Assert.Equal("European Union (rev.)", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { name = "Ghost", code = "GH" };

        var resp = await Client.PutAsJsonAsync($"/api/tradeblocs/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_NoMembers_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/tradeblocs/{DeletableId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/tradeblocs/{DeletableId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_WithMemberCountry_Returns409()
    {
        var resp = await Client.DeleteAsync($"/api/tradeblocs/{BlockedId}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/tradeblocs/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
