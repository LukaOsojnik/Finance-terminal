using System.Globalization;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

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
builder.Services.AddScoped<ISourceFieldReviewRepository, SourceFieldReviewRepository>();
builder.Services.AddScoped<IFilingRepository, FilingRepository>();

// External stock data (SEC EDGAR): typed HttpClient + the one service with real logic.
builder.Services.AddHttpClient<IStockApiClient, StockApiClient>();
builder.Services.AddScoped<IStockService, StockService>();

// Financial Modeling Prep: typed HttpClient feeding the New Company form (global fundamentals).
builder.Services.AddHttpClient<IFmpApiClient, FmpApiClient>();
// REST Countries: typed HttpClient to auto-create a Country row when FMP names one we lack.
builder.Services.AddHttpClient<IRestCountriesClient, RestCountriesClient>();
// Yahoo Finance: non-US financials fallback when FMP's income endpoint is premium-gated. Needs
// a cookie container for the crumb handshake. Frankfurter: converts that revenue to USD.
builder.Services.AddHttpClient<IYahooFinanceClient, YahooFinanceClient>()
    .ConfigurePrimaryHttpMessageHandler(() =>
        new HttpClientHandler { UseCookies = true, CookieContainer = new System.Net.CookieContainer() });
// ExchangeRate-API: converts non-US revenue to USD (~160 currencies, no key).
builder.Services.AddHttpClient<IExchangeRateApiClient, ExchangeRateApiClient>();

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
