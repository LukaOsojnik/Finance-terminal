using System.Globalization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.IoCore;
using simple_bloomberg_terminal.Models.Entities;
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

// ASP.NET Core Identity: cookie auth + EF user/role stores backed by AppDbContext (now an
// IdentityDbContext). AddDefaultIdentity wires the default Razor UI (Register/Login/Logout/
// ExternalLogin under /Identity/Account/*); AddRoles enables role-based [Authorize]. Relaxed
// password + no email confirmation so local register/login works out of the box for this lab.
builder.Services
    .AddDefaultIdentity<AppUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequireUppercase = false;
        options.Password.RequiredLength = 6;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Google external login. Credentials come from user-secrets / config under Authentication:Google;
// only registered when both are present so the app still boots without them (the Google button
// just won't appear).
var googleClientId = builder.Configuration["Authentication:Google:ClientId"];
var googleClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
{
    builder.Services.AddAuthentication()
        .AddGoogle(options =>
        {
            options.ClientId = googleClientId;
            options.ClientSecret = googleClientSecret;
        });
}

builder.Services.AddRazorPages();

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
builder.Services.AddScoped<ICompanyRiskRepository, CompanyRiskRepository>();
builder.Services.AddScoped<ISourceFieldReviewRepository, SourceFieldReviewRepository>();
builder.Services.AddScoped<IFilingRepository, FilingRepository>();

// External stock data (SEC EDGAR): typed HttpClient + the one service with real logic.
builder.Services.AddHttpClient<IStockApiClient, StockApiClient>();
builder.Services.AddScoped<IStockService, StockService>();

// DeepSeek: typed HttpClient shared by the phase-2 reviewer (Mode A) and the filing extractor
// (Mode B) on the /extraction page.
builder.Services.AddHttpClient<IDeepSeekClient, DeepSeekClient>();
builder.Services.AddScoped<IReviewService, ReviewService>();
// sec2md sidecar (Python): converts a filing to clean markdown before the extractor's heading triage.
builder.Services.AddHttpClient<ISec2MdClient, Sec2MdClient>();
builder.Services.AddScoped<IFilingExtractionService, FilingExtractionService>();
builder.Services.AddScoped<IExtractionChatService, ExtractionChatService>();
// Perplexity sonar: typed HttpClient that web-searches a company's named suppliers/customers — the
// counterparties SEC filings don't disclose. Feeds the "Discover related companies" action.
builder.Services.AddHttpClient<ICounterpartyDiscovery, CounterpartyDiscoveryService>();
// Perplexity sonar: typed HttpClient that web-searches a single private company's profile (sector,
// industry, country, description, estimated financials) — the New Company "Private (AI)" path.
builder.Services.AddHttpClient<ICompanyProfileDiscovery, CompanyProfileDiscoveryService>();
// Shared DeepSeek-backed GICS industry classifier (New Company fetch, private discovery, and the
// ticker-less counterparty stub all need to pick an industry within a sector).
builder.Services.AddScoped<IIndustryClassifier, IndustryClassifier>();
// Caches a filing's cleaned section text so each chat turn doesn't re-download the document.
builder.Services.AddMemoryCache();
// Tracks detached auto-scan jobs (started on the extraction page, run in the background) so the
// notification widget can poll their status from any page. Singleton: server-wide shared state.
builder.Services.AddSingleton<ScanJobStore>();
builder.Services.AddSingleton<RediscoverJobStore>();

// Input-output cascade model: load the matrix artifact once at startup and validate every
// Section-6 invariant — a model that violates Hawkins–Simon fails the app loudly here rather than
// producing nonsense rankings later. Singleton: the validated matrices/solver are immutable and
// shared server-wide. EventImpactService is Scoped because it reads companies via the DbContext.
builder.Services.AddSingleton(_ => IoModelLoader.LoadFromFile(
    Path.Combine(builder.Environment.ContentRootPath, "IoCore", "Data", "io_model_v1.json")));
builder.Services.AddScoped<EventImpactService>();

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
// Assembles dated financial history from FMP's statements (Yahoo fallback). Consumes the typed
// FMP/Yahoo clients above, so a plain scoped service — not its own HttpClient.
builder.Services.AddScoped<ICompanyFinancialsService, CompanyFinancialsService>();

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
app.UseAuthentication();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();
app.MapRazorPages();

// Seed the three roles (Admin, Manager, User) once at startup so role-based [Authorize] has
// something to match. Idempotent — skips any role that already exists.
using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Manager", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }
}

app.Run();

// Exposed so the integration-test WebApplicationFactory<Program> can boot the app.
public partial class Program { }
