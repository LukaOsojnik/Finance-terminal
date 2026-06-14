using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Data;

public class AppDbContext : IdentityDbContext<AppUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Country> Countries { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<TradeBloc> TradeBlocs { get; set; }
    public DbSet<RevenueSource> RevenueSources { get; set; }
    public DbSet<CostSource> CostSources { get; set; }
    public DbSet<CompanyRisk> CompanyRisks { get; set; }
    public DbSet<CompanyFinancial> CompanyFinancials { get; set; }
    public DbSet<CountryDetails> CountryDetails { get; set; }
    public DbSet<CountryAdvantage> CountryAdvantages { get; set; }
    public DbSet<CountryChallenge> CountryChallenges { get; set; }
    public DbSet<GdpSnapshot> GdpSnapshots { get; set; }
    public DbSet<SourceFieldReview> SourceFieldReviews { get; set; }
    public DbSet<Filing> Filings { get; set; }
    public DbSet<Scenario> Scenarios { get; set; }
    public DbSet<ScenarioShock> ScenarioShocks { get; set; }
    public DbSet<UserApiKey> UserApiKeys { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Country>()
            .HasOne(c => c.Details)
            .WithOne(d => d.Country)
            .HasForeignKey<CountryDetails>(d => d.CountryId);

        modelBuilder.Entity<SourceFieldReview>(e =>
        {
            e.HasOne(r => r.Company)
                .WithMany()
                .HasForeignKey(r => r.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.RevenueSource)
                .WithMany(rs => rs.Reviews)
                .HasForeignKey(r => r.RevenueSourceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.CostSource)
                .WithMany(cs => cs.Reviews)
                .HasForeignKey(r => r.CostSourceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(r => r.CompanyRisk)
                .WithMany(cr => cr.Reviews)
                .HasForeignKey(r => r.CompanyRiskId)
                .OnDelete(DeleteBehavior.Restrict);

            // The filing this per-field proof was drawn from (one source -> many filings via its
            // reviews). Restrict so a referenced filing can't be hard-deleted from under a review.
            e.HasOne(r => r.Filing)
                .WithMany()
                .HasForeignKey(r => r.FilingId)
                .OnDelete(DeleteBehavior.Restrict);

            // one current reference per cell; MySQL allows multiple NULLs so
            // cost rows (null RevenueSourceId) and revenue rows (null CostSourceId) don't collide
            e.HasIndex(r => new { r.RevenueSourceId, r.Field }).IsUnique();
            e.HasIndex(r => new { r.CostSourceId, r.Field }).IsUnique();
            e.HasIndex(r => new { r.CompanyRiskId, r.Field }).IsUnique();

            // Exactly one of the three source FKs is set (the others NULL). Booleans are 0/1 in
            // MySQL, so the non-null count must sum to exactly 1.
            e.ToTable(t => t.HasCheckConstraint(
                "CK_SourceFieldReview_OneSource",
                "((RevenueSourceId IS NOT NULL) + (CostSourceId IS NOT NULL) + (CompanyRiskId IS NOT NULL)) = 1"));
        });

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

        modelBuilder.Entity<Filing>(e =>
        {
            e.HasOne(f => f.Company)
                .WithMany()
                .HasForeignKey(f => f.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);

            // EDGAR accession numbers are globally unique => one Filing row per filing,
            // shared by every review (and thus source) it backs (upsert-by-accession in
            // ExtractionController).
            e.HasIndex(f => f.AccessionNumber).IsUnique();
        });

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
