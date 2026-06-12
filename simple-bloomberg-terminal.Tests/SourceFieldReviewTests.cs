using System.Net;
using System.Net.Http.Json;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>Integration suite for <c>SourceFieldReviewsController</c> (plain uniform CRUD).</summary>
public class SourceFieldReviewTests : ApiTestBase
{
    private const long ReviewId = CustomWebApplicationFactory.SourceFieldReviewId; // on Apple
    private const long AppleId = CustomWebApplicationFactory.CompanyAppleId;
    private const long RiskId = CustomWebApplicationFactory.CompanyRiskId; // the one source FK we attach to
    private const long MissingId = CustomWebApplicationFactory.MissingId;

    [Fact]
    public async Task GetAll_ReturnsSeeded()
    {
        var items = await Client.GetFromJsonAsync<List<SourceFieldReviewDto>>("/api/sourcefieldreviews");

        Assert.NotNull(items);
        Assert.Contains(items!, r => r.ReferencePointer == "us-gaap/Revenues/2023");
    }

    [Fact]
    public async Task GetById_Existing_ReturnsReview()
    {
        var resp = await Client.GetAsync($"/api/sourcefieldreviews/{ReviewId}");
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<SourceFieldReviewDto>();
        Assert.Equal(AppleId, dto!.CompanyId);
        Assert.Equal(RelationKind.RISK, dto.Relation);
        Assert.Equal(ReviewableField.VALUE, dto.Field);
    }

    [Fact]
    public async Task GetById_Missing_Returns404()
    {
        var resp = await Client.GetAsync($"/api/sourcefieldreviews/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Create_Valid_Returns201_WithLocation()
    {
        // Exactly one source FK (CompanyRiskId); Field=NOTE to avoid the (CompanyRiskId, Field)
        // unique index already taken by the seeded VALUE row.
        var body = new
        {
            companyId = AppleId,
            relation = RelationKind.RISK,
            companyRiskId = RiskId,
            field = ReviewableField.NOTE,
            endpoint = "company-facts",
            referencePointer = "risk/note/1",
            referenceSnapshot = "FX risk note"
        };

        var resp = await Client.PostAsJsonAsync("/api/sourcefieldreviews", body);
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        Assert.NotNull(resp.Headers.Location);

        var dto = await resp.Content.ReadFromJsonAsync<SourceFieldReviewDto>();
        Assert.True(dto!.Id > 0);
        Assert.Equal("risk/note/1", dto.ReferencePointer);
    }

    [Fact]
    public async Task Create_MissingRequired_Returns400()
    {
        var body = new { companyId = AppleId }; // missing Relation, Field, Endpoint, ReferencePointer, ReferenceSnapshot

        var resp = await Client.PostAsJsonAsync("/api/sourcefieldreviews", body);

        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Update_Existing_Returns200_WithChangedSnapshot()
    {
        var body = new
        {
            companyId = AppleId,
            relation = RelationKind.RISK,
            companyRiskId = RiskId,
            field = ReviewableField.VALUE,
            endpoint = "company-facts",
            referencePointer = "us-gaap/Revenues/2023",
            referenceSnapshot = "Total net sales 391,035"
        };

        var resp = await Client.PutAsJsonAsync($"/api/sourcefieldreviews/{ReviewId}", body);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);

        var dto = await resp.Content.ReadFromJsonAsync<SourceFieldReviewDto>();
        Assert.Equal("Total net sales 391,035", dto!.ReferenceSnapshot);
    }

    [Fact]
    public async Task Update_Missing_Returns404()
    {
        var body = new
        {
            companyId = AppleId,
            relation = RelationKind.RISK,
            companyRiskId = RiskId,
            field = ReviewableField.VALUE,
            endpoint = "company-facts",
            referencePointer = "us-gaap/Revenues/2023",
            referenceSnapshot = "x"
        };

        var resp = await Client.PutAsJsonAsync($"/api/sourcefieldreviews/{MissingId}", body);

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task Delete_Existing_Returns204_ThenGone()
    {
        var resp = await Client.DeleteAsync($"/api/sourcefieldreviews/{ReviewId}");
        Assert.Equal(HttpStatusCode.NoContent, resp.StatusCode);

        var after = await Client.GetAsync($"/api/sourcefieldreviews/{ReviewId}");
        Assert.Equal(HttpStatusCode.NotFound, after.StatusCode);
    }

    [Fact]
    public async Task Delete_Missing_Returns404()
    {
        var resp = await Client.DeleteAsync($"/api/sourcefieldreviews/{MissingId}");
        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}
