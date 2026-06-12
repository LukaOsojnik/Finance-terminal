using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>CompanyRisksController</c> (plain uniform CRUD).</summary>
public class CompanyRiskTests : ApiTestBase
{
    private const long RiskId = CustomWebApplicationFactory.CompanyRiskId; // on Apple
    private const long AppleId = CustomWebApplicationFactory.CompanyAppleId;
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<CompanyRiskDto>>("/api/companyrisks");

        Assert.NotNull(items);
        Assert.Contains(items!, r => r.Name == "FX exposure");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsRisk()
    {
        var resp = await Client.GetAsync($"/api/companyrisks/{RiskId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyRiskDto>();
        Assert.Equal("FX exposure", dto!.Name);
        Assert.Equal(AppleId, dto.CompanyId);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/companyrisks/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        var body = new { scope = RiskScope.INDUSTRY, name = "Supply chain", companyId = AppleId };

        var resp = await Client.PostAsJsonAsync("/api/companyrisks", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyRiskDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("Supply chain", dto.Name);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { companyId = AppleId }; // missing Scope + Name

        var resp = await Client.PostAsJsonAsync("/api/companyrisks", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedName()
    {
        var body = new { scope = RiskScope.FINANCIAL, name = "FX exposure (revised)", companyId = AppleId };

        var resp = await Client.PutAsJsonAsync($"/api/companyrisks/{RiskId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<CompanyRiskDto>();
        Assert.Equal("FX exposure (revised)", dto!.Name);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new { scope = RiskScope.FINANCIAL, name = "Nope", companyId = AppleId };

        var resp = await Client.PutAsJsonAsync($"/api/companyrisks/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/companyrisks/{RiskId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/companyrisks/{RiskId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/companyrisks/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
