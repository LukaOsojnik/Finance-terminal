using Microsoft.Extensions.DependencyInjection;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Cascade delete of a source. Proof (and thus the filing link) is per-field on SourceFieldReview,
/// so a source's filings = the distinct filings across its reviews. Deleting a source removes it,
/// its reviews, the filings its reviews cite, and every other source citing any of those filings
/// (with their reviews). Exercised at the repository level via a factory scope.
/// </summary>
public class SourceCascadeTests : ApiTestBase
{
    private const long AppleId = CustomWebApplicationFactory.CompanyDeletableId;

    private static Filing NewFiling(string accession) =>
        new() { CompanyId = AppleId, AccessionNumber = accession, Form = "10-K" };

    private static SourceFieldReview RevReview(long revId, ReviewableField field, long? filingId) =>
        new() { CompanyId = AppleId, Relation = RelationKind.REVENUE, RevenueSourceId = revId, Field = field, ReferenceSnapshot = "x", FilingId = filingId };

    private static SourceFieldReview CostReview(long costId, ReviewableField field, long? filingId) =>
        new() { CompanyId = AppleId, Relation = RelationKind.COST, CostSourceId = costId, Field = field, ReferenceSnapshot = "y", FilingId = filingId };

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

            var rev1 = new RevenueSource(SourceType.SEGMENT, "Rev A", AppleId) { DataSource = DataSource.MANUAL };
            var rev2 = new RevenueSource(SourceType.PRODUCT, "Rev B", AppleId) { DataSource = DataSource.MANUAL };
            var cost = new CostSource(CostBase.COGS, "Cost A", AppleId) { DataSource = DataSource.MANUAL };
            db.RevenueSources.AddRange(rev1, rev2);
            db.CostSources.Add(cost);
            db.SaveChanges();
            rev1Id = rev1.Id; rev2Id = rev2.Id; costId = cost.Id;

            // All three cite the same filing through a review.
            db.SourceFieldReviews.AddRange(
                RevReview(rev1Id, ReviewableField.VALUE, filingId),
                RevReview(rev2Id, ReviewableField.VALUE, filingId),
                CostReview(costId, ReviewableField.VALUE, filingId));
            db.SaveChanges();

            new FilingRepository(db).SoftDeleteSourceCluster(RelationKind.REVENUE, rev1Id);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev1Id).DeletedAt);
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev2Id).DeletedAt);   // sibling citing the filing
            Assert.NotNull(db.CostSources.Single(c => c.Id == costId).DeletedAt);       // sibling cost citing the filing
            Assert.NotNull(db.Filings.Single(f => f.Id == filingId).DeletedAt);
            Assert.All(
                db.SourceFieldReviews.Where(r => r.RevenueSourceId == rev1Id || r.RevenueSourceId == rev2Id || r.CostSourceId == costId).ToList(),
                r => Assert.NotNull(r.DeletedAt));
        }
    }

    [Fact]
    public void SoftDeleteSourceCluster_MultipleFilingsPerSource_RemovesAllOfThem()
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

            var rev1 = new RevenueSource(SourceType.SEGMENT, "Rev 1", AppleId) { DataSource = DataSource.MANUAL };
            var rev2 = new RevenueSource(SourceType.SEGMENT, "Rev 2", AppleId) { DataSource = DataSource.MANUAL };
            db.RevenueSources.AddRange(rev1, rev2);
            db.SaveChanges();
            rev1Id = rev1.Id; rev2Id = rev2.Id;

            // rev1 cites TWO filings across its fields; rev2 cites filing B only.
            db.SourceFieldReviews.AddRange(
                RevReview(rev1Id, ReviewableField.VALUE, filingAId),
                RevReview(rev1Id, ReviewableField.NAME, filingBId),
                RevReview(rev2Id, ReviewableField.VALUE, filingBId));
            db.SaveChanges();

            new FilingRepository(db).SoftDeleteSourceCluster(RelationKind.REVENUE, rev1Id);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.Filings.Single(f => f.Id == filingAId).DeletedAt);
            Assert.NotNull(db.Filings.Single(f => f.Id == filingBId).DeletedAt);   // rev1's second filing
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev1Id).DeletedAt);
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == rev2Id).DeletedAt); // pulled in via shared filing B
        }
    }

    [Fact]
    public void SoftDeleteSourceCluster_NoFiling_RemovesOnlyThatSourceAndItsReviews()
    {
        long soloId, otherId;

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var solo = new RevenueSource(SourceType.SEGMENT, "Solo A", AppleId) { DataSource = DataSource.MANUAL };
            var other = new RevenueSource(SourceType.SEGMENT, "Solo B", AppleId) { DataSource = DataSource.MANUAL };
            db.RevenueSources.AddRange(solo, other);
            db.SaveChanges();
            soloId = solo.Id; otherId = other.Id;

            db.SourceFieldReviews.Add(RevReview(soloId, ReviewableField.NAME, null));   // proof from Company Facts, no filing
            db.SaveChanges();

            new FilingRepository(db).SoftDeleteSourceCluster(RelationKind.REVENUE, soloId);
        }

        using (var scope = Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.NotNull(db.RevenueSources.Single(r => r.Id == soloId).DeletedAt);
            Assert.Null(db.RevenueSources.Single(r => r.Id == otherId).DeletedAt);   // unrelated, no shared filing
            Assert.All(
                db.SourceFieldReviews.Where(r => r.RevenueSourceId == soloId).ToList(),
                r => Assert.NotNull(r.DeletedAt));
        }
    }
}
