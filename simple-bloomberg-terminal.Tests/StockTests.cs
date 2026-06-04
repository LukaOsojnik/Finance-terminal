using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration suite for <c>StockController</c> + <c>StockService</c>, exercised through the
/// real HTTP pipeline with a faked EDGAR client (<see cref="FakeStockApiClient"/>). Apple is
/// seeded with the CIK the fake answers for; Microsoft's CIK yields null facts (-> 422).
/// </summary>
public class StockTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;   // CIK 0000320193, no sources
    private const long MicrosoftId = CustomWebApplicationFactory.CompanyBlockedId; // CIK 0000789019, unknown to fake
    private const long MissingId = CustomWebApplicationFactory.MissingId;
    private const long CountryUsId = CustomWebApplicationFactory.CountryUsId;

    // ---- refresh: happy path ---------------------------------------------------------

    [Fact]
    public async Task Refresh_AppleWithCik_PersistsEdgarSources()
    {
        var resp = await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        Assert.Single(dto!.RevenueSources);
        Assert.Contains(dto.RevenueSources, r => r.Name == "Revenue 2023");
        // COGS + OPEX from the two cost concepts.
        Assert.Equal(2, dto.CostSources.Count);
        Assert.Contains(dto.CostSources, c => c.Name == "COGS 2023");
        Assert.Contains(dto.CostSources, c => c.Name == "OPEX 2023");
    }

    [Fact]
    public async Task Refresh_DoesNotCreateFilingEvents()
    {
        // Filings are no longer ingested as events on refresh — they become Filing rows only
        // when a user references one in the extraction UI.
        await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);

        var events = await Client.GetFromJsonAsync<List<EventDto>>("/api/events");
        Assert.DoesNotContain(events!, e => e.Title == "10-K filed 2023-11-03");
        Assert.DoesNotContain(events!, e => e.Title == "8-K filed 2023-10-01");
    }

    // ---- refresh: idempotency --------------------------------------------------------

    [Fact]
    public async Task Refresh_RunTwice_DoesNotDuplicate()
    {
        await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);
        await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);

        // Assert via the list endpoints, which filter soft-deleted rows (DeletedAt == null).
        // The prior EDGAR rows are cleared before reinsert, so only one active set remains.
        var revenues = await Client.GetFromJsonAsync<List<RevenueSourceDto>>("/api/revenuesources");
        Assert.Single(revenues!, r => r.CompanyId == AppleId && r.DataSource == DataSource.EDGAR);

        var costs = await Client.GetFromJsonAsync<List<CostSourceDto>>("/api/costsources");
        Assert.Equal(2, costs!.Count(c => c.CompanyId == AppleId && c.DataSource == DataSource.EDGAR));
    }

    // ---- refresh: failure codes ------------------------------------------------------

    [Fact]
    public async Task Refresh_CompanyWithoutCik_Returns409()
    {
        // A company with no CIK is a non-filer.
        var created = await Client.PostAsJsonAsync("/api/companies", new
        {
            name = "Aramco",
            countryId = CountryUsId,
            sector = simple_bloomberg_terminal.Models.Enums.Sector.ENERGY
        });
        var company = await created.Content.ReadFromJsonAsync<CompanyDto>();

        var resp = await Client.PostAsync($"/api/stock/refresh/{company!.Id}", null);
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_CikNotAnSecFiler_Returns422()
    {
        // Microsoft has a CIK, but the fake EDGAR client returns null facts for it.
        var resp = await Client.PostAsync($"/api/stock/refresh/{MicrosoftId}", null);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Refresh_MissingCompany_Returns404()
    {
        var resp = await Client.PostAsync($"/api/stock/refresh/{MissingId}", null);
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- resolve ---------------------------------------------------------------------

    [Fact]
    public async Task Resolve_KnownTicker_ReturnsCik()
    {
        var resp = await Client.GetAsync("/api/stock/resolve/AAPL");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var body = await resp.Content.ReadFromJsonAsync<ResolveResult>();
        Assert.Equal("0000320193", body!.Cik);
    }

    [Fact]
    public async Task Resolve_UnknownTicker_Returns404()
    {
        var resp = await Client.GetAsync("/api/stock/resolve/ZZZZ");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    // ---- read-only EDGAR browser (right pane) ----------------------------------------

    [Fact]
    public async Task Facts_AppleWithCik_ReturnsRawJson()
    {
        var resp = await Client.GetAsync($"/api/stock/facts/{AppleId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var json = await resp.Content.ReadAsStringAsync();
        Assert.Contains("us-gaap", json);   // raw SEC payload, not a CompanyDto
    }

    [Fact]
    public async Task Facts_CikNotAnSecFiler_Returns422()
    {
        var resp = await Client.GetAsync($"/api/stock/facts/{MicrosoftId}");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, resp.StatusCode);
    }

    [Fact]
    public async Task Filings_AppleWithCik_ListsFilingsWithDocumentUrl()
    {
        var resp = await Client.GetAsync($"/api/stock/filings/{AppleId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var filings = await resp.Content.ReadFromJsonAsync<List<FilingRow>>();
        Assert.Contains(filings!, f => f.Form == "10-K" && f.PrimaryDocument == "aapl-20230930.htm");
        var tenK = filings!.First(f => f.Form == "10-K");
        // accession dashes stripped in the archive URL.
        Assert.Equal("https://www.sec.gov/Archives/edgar/data/320193/000032019323000106/aapl-20230930.htm",
            tenK.DocumentUrl);
    }

    [Fact]
    public async Task Filing_AppleDocument_ReturnsText()
    {
        var resp = await Client.GetAsync(
            $"/api/stock/filing/{AppleId}?accession=0000320193-23-000106&doc=aapl-20230930.htm");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadAsStringAsync();
        Assert.Contains("Apple Inc.", body);
    }

    [Fact]
    public async Task Filing_MissingQueryParams_Returns400()
    {
        var resp = await Client.GetAsync($"/api/stock/filing/{AppleId}");
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    private record ResolveResult(string Ticker, string Cik);
    private record FilingRow(string Form, string? FilingDate, string? AccessionNumber,
        string? PrimaryDocument, string? DocumentUrl);
}
