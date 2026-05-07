using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Repositories;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews();

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// Scoped = one instance per HTTP request (was Singleton — Singleton cannot hold a Scoped DbContext).
// Spring equivalent: @Transactional method scope vs application-scoped bean.
builder.Services.AddScoped<ICountryRepository, CountryRepository>();
builder.Services.AddScoped<ICompanyRepository, CompanyRepository>();
builder.Services.AddScoped<IEventRepository, EventRepository>();
builder.Services.AddScoped<ICountryDetailsRepository, CountryDetailsRepository>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();
app.MapStaticAssets();
app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
