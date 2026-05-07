using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Country> Countries { get; set; }
    public DbSet<Company> Companies { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<TradeBloc> TradeBlocs { get; set; }
    public DbSet<RevenueSource> RevenueSources { get; set; }
    public DbSet<CostSource> CostSources { get; set; }
    public DbSet<CountryDetails> CountryDetails { get; set; }
    public DbSet<CountryAdvantage> CountryAdvantages { get; set; }
    public DbSet<CountryChallenge> CountryChallenges { get; set; }
    public DbSet<GdpSnapshot> GdpSnapshots { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Country>()
            .HasOne(c => c.Details)
            .WithOne(d => d.Country)
            .HasForeignKey<CountryDetails>(d => d.CountryId);
    }
}
