using System.Globalization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.IoCore;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;
using simple_bloomberg_terminal.Services;

var builder = WebApplication.CreateBuilder(args);
// MissingApiKeyExceptionFilter converts a keyed client's "no user key" exception into a 424 the
// front-end turns into an "add your key" popup. Registered globally so every AJAX action is covered.
builder.Services.AddControllersWithViews(o => o.Filters.Add<simple_bloomberg_terminal.Services.MissingApiKeyExceptionFilter>());
builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly);

// Swagger/OpenAPI: docs + test UI for the Controllers/Api/* endpoints at /swagger.
// [Authorize] endpoints use cookie auth, so they're testable from the UI once logged in via the app
// (same browser session shares the auth cookie).
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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

// Identity's cookie handler answers an unauthenticated request with a 302 redirect to the login
// page. That's correct for a full-page click (the browser follows it and the user sees the login
// form), but a fetch() ALSO silently follows the 302, lands on the login HTML as a 200, and resolves
// `response.ok === true` — so AJAX features (discover / extract / delete) fail silently for logged-out
// users. Return a bare 401/403 for non-navigation requests (an /api path, an XHR header, or an Accept
// that doesn't want HTML) so site.js can detect "not logged in" and surface the sign-in prompt;
// real browser navigations still get the redirect. This is the ASP.NET equivalent of Spring
// Security's AuthenticationEntryPoint returning 401 for API paths instead of redirecting.
builder.Services.ConfigureApplicationCookie(options =>
{
    static bool IsAjax(HttpRequest r) =>
        r.Path.StartsWithSegments("/api") ||
        r.Headers.XRequestedWith == "XMLHttpRequest" ||
        !r.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);

    options.Events.OnRedirectToLogin = ctx =>
    {
        if (IsAjax(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        if (IsAjax(ctx.Request)) ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        else ctx.Response.Redirect(ctx.RedirectUri);
        return Task.CompletedTask;
    };
});

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

// Bring-your-own API keys: each user stores their own DeepSeek / FMP / Perplexity keys (encrypted
// at rest via Data Protection). The provider resolves the current user's keys per request from the
// auth cookie; the keyed clients read it instead of a global config key. HttpContextAccessor lets
// the scoped provider see the signed-in user; AddDataProtection is idempotent (also used by
// antiforgery) and makes the key-ring explicit.
builder.Services.AddDataProtection();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IUserApiKeyRepository, UserApiKeyRepository>();
builder.Services.AddScoped<IUserApiKeyProvider, UserApiKeyProvider>();
// Stores/validates the signed-in user's profile picture on disk (wwwroot/uploads/profiles). Keeps all
// File/Directory access out of AccountController; limits/types come from the "ProfilePicture" config.
builder.Services.AddScoped<ProfilePictureService>();

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
builder.Services.AddScoped<ICompanyFinancialRepository, CompanyFinancialRepository>();
builder.Services.AddScoped<IScenarioRepository, ScenarioRepository>();
builder.Services.AddScoped<IScenarioShockRepository, ScenarioShockRepository>();
builder.Services.AddScoped<ISourceFieldReviewRepository, SourceFieldReviewRepository>();
builder.Services.AddScoped<IFilingRepository, FilingRepository>();
// Owns the contribution Approve/Reject state machine over the three reviewed repos (revenue/cost/risk).
builder.Services.AddScoped<IContributionWriter, ContributionWriter>();

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
// Owns the FMP->enrich->financials->industry/country pipeline that turns a ticker (or a web-searched
// name) into a company — shared by the New Company form, bulk backfill, and counterparty linking.
builder.Services.AddScoped<ICompanyProvisioningService, CompanyProvisioningService>();
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

// Shared FMP profile -> create-model enrichment (AsOf, industry LLM, Yahoo financials). Consumes the
// classifier + typed Yahoo/exchange clients above; used by both the New Company fetch and the link path.
builder.Services.AddScoped<ITickerProfileEnricher, TickerProfileEnricher>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseSwagger();
app.UseSwaggerUI();

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
    var sp = scope.ServiceProvider;
    var roleManager = sp.GetRequiredService<RoleManager<IdentityRole>>();
    foreach (var role in new[] { "Admin", "Manager", "User" })
    {
        if (!await roleManager.RoleExistsAsync(role))
            await roleManager.CreateAsync(new IdentityRole(role));
    }

    // One-time migration of the formerly-global API keys into the new per-user store: seed the
    // developer account with whatever keys are still in config, so local dev keeps working after the
    // bring-your-own-key switch. Idempotent — skips once the account already has any key stored.
    var userManager = sp.GetRequiredService<UserManager<AppUser>>();
    var dev = await userManager.FindByEmailAsync("lukaosojnikinfo@gmail.com");
    if (dev is not null)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var row = await db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == dev.Id);
        var hasAny = row is not null &&
            (row.DeepSeekKey != null || row.FmpKey != null || row.PerplexityKey != null);
        if (!hasAny)
        {
            var protector = sp.GetRequiredService<IDataProtectionProvider>()
                .CreateProtector(UserApiKeyProvider.Purpose);
            string? Enc(string? raw) => string.IsNullOrWhiteSpace(raw) ? null : protector.Protect(raw);

            row ??= new UserApiKey { UserId = dev.Id };
            row.DeepSeekKey = Enc(app.Configuration["DeepSeek:ApiKey"]);
            row.FmpKey = Enc(app.Configuration["Fmp:ApiKey"]);
            row.PerplexityKey = Enc(app.Configuration["Perplexity:ApiKey"]);
            if (db.Entry(row).State == EntityState.Detached) db.UserApiKeys.Add(row);
            await db.SaveChangesAsync();
        }
    }
}

app.Run();

// Exposed so the integration-test WebApplicationFactory<Program> can boot the app.
public partial class Program { }
