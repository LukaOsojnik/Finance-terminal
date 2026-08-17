using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Data;

public class AppDbContext : IdentityDbContext<AppUser>, IDataProtectionKeyContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<DataProtectionKey> DataProtectionKeys { get; set; }

    public DbSet<Country> Countries { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<TradeBloc> TradeBlocs { get; set; }
    public DbSet<RevenueSource> RevenueSources { get; set; }
    public DbSet<CostSource> CostSources { get; set; }
    public DbSet<CompanyRisk> CompanyRisks { get; set; }
    public DbSet<CompanyFinancial> CompanyFinancials { get; set; }
    public DbSet<CompanyVolumeHistory> CompanyVolumeHistories { get; set; }
    public DbSet<CountryDetails> CountryDetails { get; set; }
    public DbSet<CountryAdvantage> CountryAdvantages { get; set; }
    public DbSet<CountryChallenge> CountryChallenges { get; set; }
    public DbSet<GdpSnapshot> GdpSnapshots { get; set; }
    public DbSet<Filing> Filings { get; set; }
    public DbSet<Scenario> Scenarios { get; set; }
    public DbSet<ScenarioShock> ScenarioShocks { get; set; }
    public DbSet<UserApiKey> UserApiKeys { get; set; }
    public DbSet<StockIndex> StockIndices { get; set; }
    public DbSet<IndexConstituent> IndexConstituents { get; set; }
    public DbSet<IndexImportJob> IndexImportJobs { get; set; }
    public DbSet<FmpIndustryMapping> FmpIndustryMappings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Country>()
            .HasOne(c => c.Details)
            .WithOne(d => d.Country)
            .HasForeignKey<CountryDetails>(d => d.CountryId);

        // The filing a source row's Reference/Evidence were taken from. Restrict so a cited filing
        // can't be hard-deleted from under a source row.
        modelBuilder.Entity<RevenueSource>()
            .HasOne(x => x.Filing).WithMany()
            .HasForeignKey(x => x.FilingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CostSource>()
            .HasOne(x => x.Filing).WithMany()
            .HasForeignKey(x => x.FilingId).OnDelete(DeleteBehavior.Restrict);
        modelBuilder.Entity<CompanyRisk>()
            .HasOne(x => x.Filing).WithMany()
            .HasForeignKey(x => x.FilingId).OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CompanyFinancial>(e =>
        {
            e.HasOne(f => f.Company)
                .WithMany(c => c.Financials)
                .HasForeignKey(f => f.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per company per fiscal period — the upsert key. Re-fetching a company
            // refreshes these rows in place instead of duplicating history.
            e.HasIndex(f => new { f.CompanyId, f.FiscalYear, f.Period }).IsUnique();
        });

        modelBuilder.Entity<CompanyVolumeHistory>(e =>
        {
            e.HasOne(v => v.Company)
                .WithMany(c => c.VolumeHistory)
                .HasForeignKey(v => v.CompanyId)
                .OnDelete(DeleteBehavior.Cascade);

            // One row per company per week — the upsert key. Re-backfilling refreshes these rows
            // in place instead of duplicating the time series.
            e.HasIndex(v => new { v.CompanyId, v.WeekStart }).IsUnique();
        });

        modelBuilder.Entity<Filing>(e =>
        {
            e.HasOne(f => f.Company)
                .WithMany()
                .HasForeignKey(f => f.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // EDGAR accession numbers are globally unique => one Filing row per filing,
            // shared by every source row that cites it (upsert-by-accession in ExtractionController).
            e.HasIndex(f => f.AccessionNumber).IsUnique();
        });

        // Contribution provenance: the user who proposed a pending revenue/cost/risk row. Optional FK
        // (null = system/admin write); SetNull on user-delete so deleting an account doesn't drop the
        // pending rows it contributed — they just lose the "who" and a Manager still rules on them.
        modelBuilder.Entity<RevenueSource>()
            .HasOne(r => r.ContributedBy).WithMany()
            .HasForeignKey(r => r.ContributedByUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<CostSource>()
            .HasOne(c => c.ContributedBy).WithMany()
            .HasForeignKey(c => c.ContributedByUserId).OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<CompanyRisk>()
            .HasOne(r => r.ContributedBy).WithMany()
            .HasForeignKey(r => r.ContributedByUserId).OnDelete(DeleteBehavior.SetNull);

        // Index membership as a payload-carrying N:M: the junction's own composite key
        // (StockIndexId, CompanyId) doubles as the importer's upsert key. Cascade from the index so
        // deleting an index drops its rows; Restrict from Company so an index membership can't quietly
        // hard-delete a company (companies are soft-deleted anyway).
        modelBuilder.Entity<IndexConstituent>(e =>
        {
            e.HasKey(ic => new { ic.StockIndexId, ic.CompanyId });

            e.HasOne(ic => ic.StockIndex)
                .WithMany(i => i.Constituents)
                .HasForeignKey(ic => ic.StockIndexId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ic => ic.Company)
                .WithMany()
                .HasForeignKey(ic => ic.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // One mapping row per distinct vendor label — the upsert/lookup key for the learned
        // FMP-label -> GICS sub-industry cache.
        modelBuilder.Entity<FmpIndustryMapping>()
            .HasIndex(m => m.Label).IsUnique();

        // A user's bring-your-own API keys: 1:1 with the user via a shared primary key (UserId is
        // both PK and FK). Cascade-delete so the keys vanish when the account is removed.
        modelBuilder.Entity<UserApiKey>(e =>
        {
            e.HasKey(k => k.UserId);
            e.HasOne<AppUser>()
                .WithOne()
                .HasForeignKey<UserApiKey>(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
