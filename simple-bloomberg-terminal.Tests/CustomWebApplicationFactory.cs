using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Boots the real app but swaps the MySQL DbContext for a private in-memory SQLite
/// database, seeded with deterministic rows. Each instance owns its own kept-open
/// connection => its own isolated database (a fresh factory per test method gives full
/// test isolation). Real SQL runs, so repository <c>EF.Functions.Like</c> searches work.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    // Known seeded ids (DB is fresh per factory, so identity values are deterministic).
    public const long CountryUsId = 1;
    public const long CountryDeId = 2;
    public const long CompanyDeletableId = 1;   // no sources -> SoftDelete succeeds
    public const long CompanyBlockedId = 2;     // has a revenue source -> SoftDelete 409
    public const long EventId = 1;
    public const long TradeBlocDeletableId = 1; // no member countries -> succeeds
    public const long TradeBlocBlockedId = 2;   // has a member country -> 409
    public const long CountryAdvantageId = 1;
    public const long CountryChallengeId = 1;
    public const long GdpSnapshotId = 1;
    public const long RevenueSourceId = 1;

    public const long MissingId = 999999;       // never seeded

    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CustomWebApplicationFactory()
    {
        _connection.Open(); // keep open for the lifetime of the in-memory database
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureServices(services =>
        {
            // Drop the app's MySQL AppDbContext registration (options + context + EF9 config).
            var toRemove = services.Where(d =>
                    (d.ServiceType.FullName?.Contains(nameof(AppDbContext)) ?? false) ||
                    d.ServiceType == typeof(DbContextOptions))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            // Swap the real EDGAR HttpClient for a deterministic fake (no live SEC calls).
            services.RemoveAll<IStockApiClient>();
            services.AddScoped<IStockApiClient, FakeStockApiClient>();
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Database.EnsureCreated();
        Seed(db);

        return host;
    }

    private static void Seed(AppDbContext db)
    {
        var us = new Country("US", "United States", "Americas", "USD")
            { GdpUsd = 25_000_000_000_000, Population = 331_000_000, RiskRating = 1.2 };
        var de = new Country("DE", "Germany", "Europe", "EUR")
            { GdpUsd = 4_000_000_000_000, Population = 83_000_000 };
        db.Countries.AddRange(us, de);
        db.SaveChanges();

        var apple = new Company("Apple", us.Id, Sector.INFORMATION_TECHNOLOGY)
            { Cik = "0000320193", RevenueTotal = 383_000_000_000, GrossMargin = 0.44 };
        var microsoft = new Company("Microsoft", us.Id, Sector.INFORMATION_TECHNOLOGY)
            { Cik = "0000789019" };
        db.Companies.AddRange(apple, microsoft);
        db.SaveChanges();

        // Revenue source on Microsoft -> blocks its deletion (business rule).
        db.RevenueSources.Add(new RevenueSource(SourceType.SEGMENT, "Cloud", microsoft.Id)
            { Value = 100_000_000_000, DataSource = DataSource.MANUAL });

        var evt = new Event("Apple Q4 Earnings", EventType.EARNINGS, new DateTime(2024, 11, 1))
        {
            Description = "Quarterly results",
            ImpactScore = 1.5,
            Countries = { us },
            Companies = { apple }
        };
        db.Events.Add(evt);

        // EU: no member countries -> deletable. NAFTA: has a member -> delete blocked.
        db.TradeBlocs.Add(new TradeBloc("European Union", "EU"));
        db.TradeBlocs.Add(new TradeBloc("North American FTA", "NAFTA") { Countries = { us } });

        db.CountryDetails.Add(new CountryDetails { CountryId = us.Id, MarketPosition = "Global leader" });
        db.CountryAdvantages.Add(new CountryAdvantage { CountryId = us.Id, Text = "Deep capital markets" });
        db.CountryChallenges.Add(new CountryChallenge { CountryId = us.Id, Text = "High public debt" });
        db.GdpSnapshots.Add(new GdpSnapshot { CountryId = us.Id, Year = 2023, GdpUsd = 24_000_000_000_000 });

        db.SaveChanges();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _connection.Dispose();
    }
}
