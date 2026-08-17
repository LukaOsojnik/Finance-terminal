using Microsoft.Extensions.DependencyInjection;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Cascade delete of a source. Each source row cites one filing (FilingId on the row), so deleting a
/// source removes it, that filing, and every other source citing the same filing. Exercised at the
/// repository level via a factory scope.
/// </summary>
public class SourceCascadeTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    private static Filing NewFiling(string accession) =>
        new() { CompanyId = AppleId, AccessionNumber = accession, Form = "10-K" };

    [Fact]
    public void SoftDeleteSourceCluster_RemovesEveryoneCitingTheSharedFiling()
    {
        long rev1Id, rev2Id, costId, filingId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var filing = NewFiling("ACC-SHARED");
            db.Filings.Add(filing);
            db.SaveChanges();
            filingId = filing.Id;

            // All three cite the same filing.
            var rev1 = new RevenueSource(SourceType.SEGMENT, "Rev A", AppleId)
                { DataSource = DataSource.MANUAL, FilingId = filingId, Evidence = "x" };
            var rev2 = new RevenueSource(SourceType.PRODUCT, "Rev B", AppleId)
                { DataSource = DataSource.MANUAL, FilingId = filingId, Evidence = "x" };
            var cost = new CostSource(CostBase.COGS, "Cost A", AppleId)
                { DataSource = DataSource.MANUAL, FilingId = filingId, Evidence = "y" };
            db.RevenueSources.AddRange(rev1, rev2);
            db.CostSources.Add(cost);
            db.SaveChanges();
            rev1Id = rev1.Id; rev2Id = rev2.Id; costId = cost.Id;

            new FilingRepository(db).SoftDeleteSourceCluster(ExtractionNode.REVENUE, rev1Id);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev1Id).DeletedAt);
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev2Id).DeletedAt);   // sibling citing the filing
            Assert.NotNull(db.CostSources.Single(c => c.Id == costId).DeletedAt);       // sibling cost citing the filing
            Assert.NotNull(db.Filings.Single(f => f.Id == filingId).DeletedAt);
        }
    }

    [Fact]
    public void SoftDeleteSourceCluster_OtherFilingsUntouched()
    {
        long rev1Id, rev2Id, filingAId, filingBId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fa = NewFiling("ACC-A");
            var fb = NewFiling("ACC-B");
            db.Filings.AddRange(fa, fb);
            db.SaveChanges();
            filingAId = fa.Id; filingBId = fb.Id;

            // rev1 cites filing A, rev2 cites filing B — different clusters.
            var rev1 = new RevenueSource(SourceType.SEGMENT, "Rev 1", AppleId)
                { DataSource = DataSource.MANUAL, FilingId = filingAId };
            var rev2 = new RevenueSource(SourceType.SEGMENT, "Rev 2", AppleId)
                { DataSource = DataSource.MANUAL, FilingId = filingBId };
            db.RevenueSources.AddRange(rev1, rev2);
            db.SaveChanges();
            rev1Id = rev1.Id; rev2Id = rev2.Id;

            new FilingRepository(db).SoftDeleteSourceCluster(ExtractionNode.REVENUE, rev1Id);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.Filings.Single(f => f.Id == filingAId).DeletedAt);
            Assert.Null(db.Filings.Single(f => f.Id == filingBId).DeletedAt);          // other cluster survives
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev1Id).DeletedAt);
            Assert.Null(db.RevenueSources.Single(r => r.Id == rev2Id).DeletedAt);
        }
    }

    [Fact]
    public void SoftDeleteSourceCluster_NoFiling_RemovesOnlyThatSource()
    {
        long soloId, otherId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Proof from Company Facts: an evidence quote but no filing.
            var solo = new RevenueSource(SourceType.SEGMENT, "Solo A", AppleId)
                { DataSource = DataSource.MANUAL, Evidence = "from company facts" };
            var other = new RevenueSource(SourceType.SEGMENT, "Solo B", AppleId) { DataSource = DataSource.MANUAL };
            db.RevenueSources.AddRange(solo, other);
            db.SaveChanges();
            soloId = solo.Id; otherId = other.Id;

            new FilingRepository(db).SoftDeleteSourceCluster(ExtractionNode.REVENUE, soloId);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == soloId).DeletedAt);
            Assert.Null(db.RevenueSources.Single(r => r.Id == otherId).DeletedAt);   // unrelated, no shared filing
        }
    }
}
