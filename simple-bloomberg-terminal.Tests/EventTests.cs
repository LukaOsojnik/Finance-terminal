using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>EventsController</c>. The quirk here is M:N relations:
/// POST/PUT take id lists (CountryIds/CompanyIds/TradeBlocIds) and the repo applies join
/// membership; the response nests them as <see cref="RelatedRefDto"/> (id + name).
/// No business-rule 409 on delete.
/// </summary>
public class EventTests : ApiTestBase
{
    private const long EventId = CustomWebApplicationFactory.EventId; // linked to US + Apple
    private const long MissingId = CustomWebApplicationFactory.MissingId;
    private const long CountryUsId = CustomWebApplicationFactory.CountryUsId;
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    [Fact]
    public async Task GetAll_ReturnsSeededEvent()
    {
        var items = await Client.GetFromJsonAsync<List<EventDto>>("/api/events");

        Assert.NotNull(items);
        Assert.Contains(items!, e => e.Title == "Apple Q4 Earnings");
    }

    [Fact]
    public async Task GetAll_WithQuery_FiltersWithLike()
    {
        var items = await Client.GetFromJsonAsync<List<EventDto>>("/api/events?q=Apple");

        Assert.NotNull(items);
        Assert.Contains(items!, e => e.Title == "Apple Q4 Earnings");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsNestedRelations()
    {
        var resp = await Client.GetAsync($"/api/events/{EventId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<EventDto>();
        Assert.Equal(EventType.EARNINGS, dto!.Type);
        Assert.Contains(dto.Countries, c => c.Name == "United States");
        Assert.Contains(dto.Companies, c => c.Name == "Apple");
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/events/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_AndAppliesIdLists()
    {
        var body = new
        {
            title = "Fed Rate Decision",
            type = EventType.MACRO_DATA,
            date = new DateTime(2025, 3, 19),
            impactScore = 2.0,
            countryIds = new[] { CountryUsId },
            companyIds = new[] { AppleId }
        };

        var resp = await Client.PostAsJsonAsync("/api/events", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        // Re-fetch via Location: proves the join rows were persisted, not just echoed.
        var dto = await Client.GetFromJsonAsync<EventDto>(resp.Headers.Location!.ToString());
        Assert.Equal("Fed Rate Decision", dto!.Title);
        Assert.Contains(dto.Countries, c => c.Name == "United States");
        Assert.Contains(dto.Companies, c => c.Name == "Apple");
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { type = EventType.MACRO_DATA, date = new DateTime(2025, 3, 19) }; // missing Title

        var resp = await Client.PostAsJsonAsync("/api/events", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_AndReplacesMembership()
    {
        // Drop Apple, keep only US -> membership is replaced, not merged.
        var body = new
        {
            title = "Apple Q4 Earnings (rev.)",
            type = EventType.EARNINGS,
            date = new DateTime(2024, 11, 1),
            countryIds = new[] { CountryUsId },
            companyIds = Array.Empty<long>()
        };

        var resp = await Client.PutAsJsonAsync($"/api/events/{EventId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<EventDto>();
        Assert.Equal("Apple Q4 Earnings (rev.)", dto!.Title);
        Assert.Contains(dto.Countries, c => c.Name == "United States");
        Assert.Empty(dto.Companies);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { title = "Ghost", type = EventType.MACRO_DATA, date = new DateTime(2025, 1, 1) };

        var resp = await Client.PutAsJsonAsync($"/api/events/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/events/{EventId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/events/{EventId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/events/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
