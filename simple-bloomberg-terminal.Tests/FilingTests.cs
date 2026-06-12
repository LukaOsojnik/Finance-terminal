using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>FilingsController</c>. Create goes through Upsert (accession is unique).</summary>
public class FilingTests : ApiTestBase
{
    private const long FilingId = CustomWebApplicationFactory.FilingId; // on Apple
    private const long AppleId = CustomWebApplicationFactory.CompanyAppleId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;
    private const string SeededAccession = "0000320193-23-000106";

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<FilingDto>>("/api/filings");

        Assert.NotNull(items);
        Assert.Contains(items!, f => f.AccessionNumber == SeededAccession);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsFiling()
    {
        var resp = await Client.GetAsync($"/api/filings/{FilingId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<FilingDto>();
        Assert.Equal(SeededAccession, dto!.AccessionNumber);
        Assert.Equal(AppleId, dto.CompanyId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/filings/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        // New accession -> Upsert inserts a fresh row.
        var body = new { companyId = AppleId, accessionNumber = "0000320193-24-000001", form = "8-K" };

        var resp = await Client.PostAsJsonAsync("/api/filings", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<FilingDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("0000320193-24-000001", dto.AccessionNumber);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { form = "8-K" }; // missing CompanyId + AccessionNumber

        var resp = await Client.PostAsJsonAsync("/api/filings", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedForm()
    {
        var body = new { companyId = AppleId, accessionNumber = SeededAccession, form = "10-K/A" };

        var resp = await Client.PutAsJsonAsync($"/api/filings/{FilingId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<FilingDto>();
        Assert.Equal("10-K/A", dto!.Form);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { companyId = AppleId, accessionNumber = SeededAccession, form = "8-K" };

        var resp = await Client.PutAsJsonAsync($"/api/filings/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/filings/{FilingId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/filings/{FilingId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/filings/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
