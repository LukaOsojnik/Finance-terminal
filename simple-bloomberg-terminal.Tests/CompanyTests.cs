using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Full integration suite for <c>CompaniesController</c> exercised through the real HTTP
/// pipeline (routing, model binding, validation, AutoMapper, SQLite). Companies is the
/// richest controller: nested Country + RevenueSource/CostSource DTOs, graph-loading
/// GetById, and a soft-delete business rule (active revenue sources block deletion -> 409).
/// Each test runs against a fresh seeded DB (see <see cref="ApiTestBase"/>).
/// </summary>
public class CompanyTests : ApiTestBase
{
    private const long DeletableId = CustomWebApplicationFactory.CompanyDeletableId; // Apple, no sources
    private const long BlockedId = CustomWebApplicationFactory.CompanyBlockedId;     // Microsoft, has revenue
    private const long MissingId = CustomWebApplicationFactory.MissingId;
    private const long CountryUsId = CustomWebApplicationFactory.CountryUsId;

    // ---- GET (all) -------------------------------------------------------------------

    [Fact]
    public async Task GetAll_ReturnsSeededCompanies()
    {
        var items = await Client.GetFromJsonAsync<List<CompanyDto>>("/api/companies");

        Assert.NotNull(items);
        Assert.Contains(items!, c => c.Name == "Apple");
        Assert.Contains(items!, c => c.Name == "Microsoft");
    }

    [Fact]
    public async Task GetAll_WithQuery_FiltersWithLike()
    {
        var items = await Client.GetFromJsonAsync<List<CompanyDto>>("/api/companies?q=App");

        Assert.NotNull(items);
        Assert.Contains(items!, c => c.Name == "Apple");
        Assert.DoesNotContain(items!, c => c.Name == "Microsoft");
    }

    // ---- GET (by id) -----------------------------------------------------------------

    [Fact]
    public async Task GetById_Existing_ReturnsCompanyWithNestedCountry()
    {
        var resp = await Client.GetAsync($"/api/companies/{DeletableId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Equal(DeletableId, dto!.Id);
        Assert.Equal("Apple", dto.Name);
        Assert.Equal(Sector.INFORMATION_TECHNOLOGY, dto.Sector);
        Assert.NotNull(dto.Country);
        Assert.Equal("US", dto.Country!.Code);
    }

    [Fact]
    public async Task GetById_WithRevenueSource_PopulatesNestedSources()
    {
        // Microsoft is seeded with a "Cloud" RevenueSource; GetById uses the graph repo.
        var resp = await Client.GetAsync($"/api/companies/{BlockedId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Equal("Microsoft", dto!.Name);
        Assert.Contains(dto.RevenueSources, r => r.Name == "Cloud");
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/companies/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- POST ------------------------------------------------------------------------

    [Fact]
    public async Task Create_Valid_Returns201_WithLocationAndBody()
    {
        var body = new
        {
            name = "Nvidia",
            countryId = CountryUsId,
            sector = Sector.INFORMATION_TECHNOLOGY,
            cik = "0001045810",
            revenueTotal = 60_000_000_000d
        };

        var resp = await Client.PostAsJsonAsync("/api/companies", body);

        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Nvidia", dto.Name);
        Assert.NotNull(dto.Country);
        Assert.Equal("US", dto.Country!.Code);

        // Persisted: the Location URL fetches the same row back.
        var followUp = await Client.GetAsync(resp.Headers.Location);
        Assert.Equal(HttpStatusCode.OK, followUp.StatusCode);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { sector = Sector.INFORMATION_TECHNOLOGY }; // missing Name + CountryId

        var resp = await Client.PostAsJsonAsync("/api/companies", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    // ---- PUT -------------------------------------------------------------------------

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedFields()
    {
        var body = new
        {
            name = "Apple Inc.",
            countryId = CountryUsId,
            sector = Sector.INFORMATION_TECHNOLOGY,
            grossMargin = 0.46d
        };

        var resp = await Client.PutAsJsonAsync($"/api/companies/{DeletableId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Equal("Apple Inc.", dto!.Name);
        Assert.Equal(0.46d, dto.GrossMargin);

        // Persisted across a fresh GET.
        var after = await Client.GetFromJsonAsync<CompanyDto>($"/api/companies/{DeletableId}");
        Assert.Equal("Apple Inc.", after!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { name = "Ghost", countryId = CountryUsId, sector = Sector.INFORMATION_TECHNOLOGY };

        var resp = await Client.PutAsJsonAsync($"/api/companies/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- DELETE ----------------------------------------------------------------------

    [Fact]
    public async Task Delete_NoSources_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/companies/{DeletableId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        // Soft-deleted rows are excluded from GET -> 404 afterwards.
        var after = await Client.GetAsync($"/api/companies/{DeletableId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_WithActiveRevenueSource_Returns409()
    {
        var resp = await Client.DeleteAsync($"/api/companies/{BlockedId}");
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/companies/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
