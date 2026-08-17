using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Phase-1 extraction flow: refresh Apple's EDGAR rows, then freeze proof onto the revenue row.
/// Proof is one pair per row — Reference (where in the document) + Evidence (the verbatim passage) —
/// stored on the row itself along with the filing it came from.
/// </summary>
public class ExtractionTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    private record RefResult(long RevenueSourceId);

    private async Task<long> RefreshAppleAndGetRevenueRowId()
    {
        var resp = await Client.PostAsync($"/api/stock/refresh/{AppleId}", null);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var dto = await resp.Content.ReadFromJsonAsync<CompanyDto>();
        return dto!.RevenueSources.Single(r => r.DataSource == DataSource.EDGAR).Id;
    }

    [Fact]
    public async Task Reference_OnEdgarRevenueRow_WritesProofOntoTheRow()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = rowId,
            sourceType = "SEGMENT",
            name = "Revenue 2023",
            value = 383_000_000_000d,
            reference = "Item 7. Management's Discussion",
            evidence = "\"val\": 383000000000"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.Equal(rowId, result!.RevenueSourceId);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == rowId);

        Assert.Equal("Item 7. Management's Discussion", row.Reference);
        Assert.Equal("\"val\": 383000000000", row.Evidence);
        Assert.Null(row.FilingId);   // no filing was open — proof came from Company Facts
    }

    [Fact]
    public async Task Reference_SameRowTwice_OverwritesTheProofInPlace()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();

        async Task<RefResult> Ref(string evidence) =>
            (await (await Client.PostAsJsonAsync("/extraction/reference", new
            {
                companyId = AppleId,
                revenueSourceId = rowId,
                sourceType = "SEGMENT",
                name = "Revenue 2023",
                reference = "Item 8. Financial Statements",
                evidence
            })).Content.ReadFromJsonAsync<RefResult>())!;

        var first = await Ref("first proof");
        var second = await Ref("second proof");

        Assert.Equal(first.RevenueSourceId, second.RevenueSourceId);   // same row reused

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == rowId);
        Assert.Equal("second proof", row.Evidence);
    }

    [Fact]
    public async Task Reference_NoSourceRow_CreatesTheRowWithItsProof()
    {
        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = (long?)null,   // user-created row
            sourceType = "PRODUCT",
            name = "Services",
            value = 85_000_000_000d,
            reference = "Item 8, Segment note",
            evidence = "services revenue 85B"
        });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var result = await resp.Content.ReadFromJsonAsync<RefResult>();
        Assert.True(result!.RevenueSourceId > 0);

        using var scope = Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = db.RevenueSources.Single(r => r.Id == result.RevenueSourceId);
        Assert.Equal("Services", row.Name);
        Assert.Equal(DataSource.MANUAL, row.DataSource);
        Assert.Equal("services revenue 85B", row.Evidence);
    }

    [Fact]
    public async Task References_AfterReferencing_ReturnsTheRowsProof()
    {
        var rowId = await RefreshAppleAndGetRevenueRowId();
        await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = rowId,
            sourceType = "SEGMENT",
            name = "Revenue 2023",
            reference = "Item 7A. Quantitative Disclosures",
            evidence = "val 383000000000"
        });

        var proof = await Client.GetFromJsonAsync<RefRow>($"/extraction/references/{rowId}");
        Assert.Equal("Item 7A. Quantitative Disclosures", proof!.Reference);
        Assert.Equal("val 383000000000", proof.Evidence);
        Assert.Null(proof.Filing);
    }

    private record RefRow(string? Reference, string? Evidence, string? Filing);

    [Fact]
    public async Task Reference_MissingEvidence_Returns400()
    {
        var resp = await Client.PostAsJsonAsync("/extraction/reference", new
        {
            companyId = AppleId,
            revenueSourceId = (long?)null,
            sourceType = "SEGMENT",
            name = "X",
            evidence = ""
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
            reference = "Item 8", evidence = "snap",
            filingAccessionNumber = accession, filingForm = "10-K", filingDate = "2023-11-03", filingUrl = "http://x"
        });

        // 1. First reference creates the Filing and links the row to it.
        Assert.Equal(HttpStatusCode.OK, (await RefWithFiling()).StatusCode);

        long filingId;
        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var f = db.Filings.Single(x => x.AccessionNumber == accession);
            filingId = f.Id;
            Assert.Equal(filingId, db.RevenueSources.Single(r => r.Id == rowId).FilingId);
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
