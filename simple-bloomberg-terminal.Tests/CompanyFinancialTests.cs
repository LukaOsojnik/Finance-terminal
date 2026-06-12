using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>CompanyFinancialsController</c> (plain uniform CRUD).</summary>
public class CompanyFinancialTests : ApiTestBase
{
    private const long FinancialId = CustomWebApplicationFactory.CompanyFinancialId; // on Apple
    private const long AppleId = CustomWebApplicationFactory.CompanyAppleId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<CompanyFinancialDto>>("/api/companyfinancials");

        Assert.NotNull(items);
        Assert.Contains(items!, f => f.CompanyId == AppleId && f.FiscalYear == 2023);
    }

    [Fact]
    public async Task GetById_Existing_ReturnsFinancial()
    {
        var resp = await Client.GetAsync($"/api/companyfinancials/{FinancialId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyFinancialDto>();
        Assert.Equal(AppleId, dto!.CompanyId);
        Assert.Equal(FiscalPeriod.FY, dto.Period);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/companyfinancials/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { companyId = AppleId, fiscalYear = 2024, period = FiscalPeriod.Q1, revenue = 90_000_000_000d };

        var resp = await Client.PostAsJsonAsync("/api/companyfinancials", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyFinancialDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal(2024, dto.FiscalYear);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { fiscalYear = 2024 }; // missing CompanyId + Period

        var resp = await Client.PostAsJsonAsync("/api/companyfinancials", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedRevenue()
    {
        var body = new { companyId = AppleId, fiscalYear = 2023, period = FiscalPeriod.FY, revenue = 400_000_000_000d };

        var resp = await Client.PutAsJsonAsync($"/api/companyfinancials/{FinancialId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyFinancialDto>();
        Assert.Equal(400_000_000_000d, dto!.Revenue);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { companyId = AppleId, fiscalYear = 2023, period = FiscalPeriod.FY };

        var resp = await Client.PutAsJsonAsync($"/api/companyfinancials/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/companyfinancials/{FinancialId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/companyfinancials/{FinancialId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/companyfinancials/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
