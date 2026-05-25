using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();
builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
// AutoDetect opens a MySQL connection at startup; skip it under the integration-test
// host ("Testing"), which removes this provider and swaps in SQLite anyway.
var serverVersion = builder.Environment.IsEnvironment("Testing")
    ? new MySqlServerVersion(new Version(8, 0, 0))
    : ServerVersion.AutoDetect(connectionString);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, serverVersion));

// Scoped = one instance per HTTP request (was Singleton — Singleton cannot hold a Scoped DbContext).
// Spring equivalent: @Transactional method scope vs application-scoped bean.
builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ICountryDetailsRepository, CountryDetailsRepository>();
builder.Services.AddScoped<ITradeBlocRepository, TradeBlocRepository>();
builder.Services.AddScoped<ICountryAdvantageRepository, CountryAdvantageRepository>();
builder.Services.AddScoped<ICountryChallengeRepository, CountryChallengeRepository>();
builder.Services.AddScoped<IGdpSnapshotRepository, GdpSnapshotRepository>();
builder.Services.AddScoped<IRevenueSourceRepository, RevenueSourceRepository>();
builder.Services.AddScoped<ICostSourceRepository, CostSourceRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

var supportedCultures = new[]
{
    new CultureInfo("hr"),
    new CultureInfo("en-US")
};
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("hr"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Exposed so the integration-test WebApplicationFactory<Program> can boot the app.
public partial class Program { }
