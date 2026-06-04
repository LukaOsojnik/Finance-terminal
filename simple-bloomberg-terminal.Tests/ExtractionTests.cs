using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Phase-1 extraction flow: refresh Apple's EDGAR rows, then "Use as reference" on the revenue
/// row's VALUE cell. Asserts a <c>SourceFieldReview</c> lands with the right FK + Field + frozen
/// snapshot + <c>Mark==null</c>, and that re-referencing the same cell upserts in place.
/// </summary>
public class ExtractionTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    private record RefResult(long RevenueSourceId, long ReviewId, string Field);

    private async Task<long> RefreshAppleAndGetRevenueRowId()
    {
        var resp = await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        return dto!.RevenueSources.Single(r => r.DataSource == DataSource.EDGAR).Id;
    }

    [Fact]
    public async Task Reference_OnEdgarRevenueRow_WritesUnreviewedSnapshot()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = rowId,
            sourceType = "SEGMENT",
            name = "Revenue 2023",
            value = 383_000_000_000d,
            field = "VALUE",
            referencePointer = "chars 10-25",
            referenceSnapshot = "\"val\": 383000000000",
            referencedValue = "383000000000"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.Equal(rowId, result!.RevenueSourceId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var review = db.SourceFieldReviews.Single(r => r.Id == result.ReviewId);

        Assert.Equal(rowId, review.RevenueSourceId);
        Assert.Null(review.CostSourceId);
        Assert.Equal(RelationKind.REVENUE, review.Relation);
        Assert.Equal(ReviewableField.VALUE, review.Field);
        Assert.Equal("\"val\": 383000000000", review.ReferenceSnapshot);
        Assert.Null(review.Mark);   // queued for phase 2
    }

    [Fact]
    public async Task Reference_SameCellTwice_UpsertsInPlace()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        async Task<RefResult> Ref(string snapshot) =>
            (await (await Client.PostAsJsonAsync("/extraction/reference", new
            {
                companyId = AppleId,
                revenueSourceId = rowId,
                sourceType = "SEGMENT",
                name = "Revenue 2023",
                field = "NAME",
                referencePointer = "p",
                referenceSnapshot = snapshot
            })).Content.ReadFromJsonAsync<RefResult>())!;

        var first = await Ref("first proof");
        var second = await Ref("second proof");

        Assert.Equal(first.ReviewId, second.ReviewId);   // same (row, field) row reused

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rows = db.SourceFieldReviews
            .Where(r => r.RevenueSourceId == rowId && r.Field == ReviewableField.NAME && r.DeletedAt == null)
            .ToList();
        Assert.Single(rows);
        Assert.Equal("second proof", rows[0].ReferenceSnapshot);
    }

    [Fact]
    public async Task Reference_NoSourceRow_CreatesRowThenReview()
    {
        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = (long?)null,   // user-created row
            sourceType = "PRODUCT",
            name = "Services",
            value = 85_000_000_000d,
            field = "VALUE",
            referencePointer = "p",
            referenceSnapshot = "services revenue 85B"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.True(result!.RevenueSourceId > 0);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == result.RevenueSourceId);
        Assert.Equal("Services", row.Name);
        Assert.Equal(DataSource.MANUAL, row.DataSource);
        Assert.True(db.SourceFieldReviews.Any(r => r.RevenueSourceId == row.Id));
    }

    [Fact]
    public async Task References_AfterReferencing_ReturnsSavedPointer()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();
        await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = rowId,
            sourceType = "SEGMENT",
            name = "Revenue 2023",
            field = "VALUE",
            referencePointer = "chars 40-60",
            referenceSnapshot = "val 383000000000"
        });

        var refs = await Client.GetFromJsonAsync<List<RefRow>>($"/extraction/references/{rowId}");
        var v = Assert.Single(refs!);
        Assert.Equal("VALUE", v.Field);
        Assert.Equal("chars 40-60", v.Pointer);
        Assert.Equal("val 383000000000", v.Snapshot);
        Assert.Null(v.Mark);
    }

    private record RefRow(string Field, string Snapshot, string Pointer, string Endpoint, int? Mark);

    [Fact]
    public async Task Reference_MissingSnapshot_Returns400()
    {
        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = (long?)null,
            sourceType = "SEGMENT",
            name = "X",
            field = "VALUE",
            referenceSnapshot = ""
        });
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Reference_AfterFilingSoftDeleted_RevivesInsteadOfDuplicateInsert()
    {
        const string accession = "0000000000-99-000777";
        var rowId = await RefreshAppleAndGetRevenueRowId();

        Task<HttpResponseMessage> RefWithFiling() => Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId, revenueSourceId = rowId, sourceType = "SEGMENT", name = "Revenue 2023",
            field = "VALUE", referencePointer = "p", referenceSnapshot = "snap", referencedValue = "1",
            filingAccessionNumber = accession, filingForm = "10-K", filingDate = "2023-11-03", filingUrl = "http://x"
        });

        // 1. First reference creates the Filing.
        Assert.Equal(HttpStatusCode.OK, (await RefWithFiling()).StatusCode);

        long filingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var f = db.Filings.Single(x => x.AccessionNumber == accession);
            filingId = f.Id;
            f.DeletedAt = DateTime.UtcNow;   // simulate a prior source-cluster delete
            db.SaveChanges();
        }

        // 2. Re-referencing the same accession must revive the row, not insert a duplicate
        //    (which would hit the unique accession index).
        Assert.Equal(HttpStatusCode.OK, (await RefWithFiling()).StatusCode);

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var filings = db.Filings.Where(x => x.AccessionNumber == accession).ToList();
            Assert.Single(filings);                  // not duplicated
            Assert.Equal(filingId, filings[0].Id);   // same row revived
            Assert.Null(filings[0].DeletedAt);       // brought back to life
        }
    }
}
