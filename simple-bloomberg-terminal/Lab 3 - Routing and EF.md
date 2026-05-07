# Lab 3 - Routing and EF

## Routing

### Convention-based routing (Program.cs)

```csharp
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
```

Parameters:
- `{controller}` → maps to `XxxController` class
- `{action}` → maps to `IActionResult Yyy()` method
- `{id?}` → optional parameter passed to action
- Defaults: `controller=Home`, `action=Index` — used when URL segment missing
- `defaults` parameter in `MapControllerRoute` provides fallback values

| URL | Controller | Action | Id |
|---|---|---|---|
| `/Xxx/Yyy/123` | XxxController | Yyy | 123 |
| `/Xxx/Yyy` | XxxController | Yyy | null |
| `/Xxx` | XxxController | Index | null |
| `/` | HomeController | Index | null |

### MVC naming conventions

- Controller class for "Xyz" = `XyzController`
- Controllers go in `Controllers/` folder
- Views go in `Views/Xyz/` folder
- Routes reference controller as `Xyz`, not `XyzController`

### Static routes

```csharp
app.MapControllerRoute("Profile_default", "moj-profil",
    new { controller = "Account", action = "Profile" });
```

### Constraints

```csharp
app.MapControllerRoute(
    name: "BlogDetails2",
    url: "{blog}/{post}",
    defaults: new { controller = "Blog", action = "Details", post = UrlParameter.Optional },
    constraints: new {
        blog = @"[a-zA-Z0-9-]+",
        post = @"[a-zA-Z0-9-]*"
    });
```

- `[a-zA-Z0-9-]+` — at least one char
- `[a-zA-Z0-9-]*` — zero or more (optional)
- Built-in constraints: `length(n)`, `minlength(n)`, `maxlength(n)`, `int`, `guid`, etc.

### Attribute routing

Add `[Route]` on controller and/or action.

```csharp
[Route("gradovi")]
public class CityController : Controller
{
    [Route("po-drzavi/{country:length(3)}")]
    public ActionResult List(string country) { ... }
}
// → /gradovi/po-drzavi/CRO
```

Token replacement:
- `[controller]` → controller name (without "Controller")
- `[action]` → action method name

**Key examples:**

```csharp
[Route("[controller]/[action]")]          // /Home/Index
[Route("/")]                              // / (root)
[Route("{lang:minlength(1):maxlength(2)?}")] // optional lang param
[Route("")]                               // matches controller route only
```

Multiple `[Route]` attributes on same action → multiple URLs map to same action.

---

## Partial Views

- Render a reusable piece of UI (analogous to WebForms user controls)
- Do **not** render within `_Layout` — output raw content only
- Naming convention: `_PartialName.cshtml` (leading underscore, in `Shared/` folder)

### Rendering partials

```html
@await Html.PartialAsync("_LoginPartial")
<partial name="_CookieConsentPartial" />
```

### Passing models

Without explicit model, inherits parent view's model → can cause type mismatch.

```html
@await Html.PartialAsync("_ClientFilter", new ClientFilterModel())
<partial name="_ClientFilter" model="new ClientFilterModel()" />
```

Pass `null` **fails** if partial expects non-nullable model.

---

## HTML Input Generation

### HTML Helpers

```html
@Html.TextBoxFor(p => p.Ime)       <!-- always input type=text -->
@Html.EditorFor(p => p.Ime)        <!-- type depends on data type -->
@Html.NameFor(p => p.Prezime)      <!-- generates "name" attribute -->
```

### Tag Helpers

```html
<input asp-for="Ime" class="form-control" />
<!-- replaces TextBoxFor/EditorFor lambda expressions -->
```

### Form Helpers

```html
@using(Html.BeginForm()) { ... }
<form asp-action="SubmitQuery" method="post"> ... </form>
```

`BeginForm` parameters: action, controller, method (GET/POST), route params, html attributes.

---

## Entity Framework (EF)

ORM — maps DB tables to C# classes. Tracks changes. Translates LINQ → SQL.

### Project layering

- **Model** — classes representing DB tables
- **DAL** — EF context, migrations
- **Web** — controllers, views, UI

### Model conventions

```csharp
public class Quiz
{
    [Key]
    public int Id { get; set; }
    // ...
    public virtual ICollection<Question> Questions { get; set; }  // 1-N
}

public class Question
{
    [Key]
    public int Id { get; set; }
    // ...
    [ForeignKey("Quiz")]
    public int QuizId { get; set; }
    public virtual Quiz Quiz { get; set; }
}
```

**Rules:**
- Every entity: `[Key]` on `Id`
- 1-N: `virtual ICollection<T>` on "one" side
- 1-N: `[ForeignKey("Parent")]` + `ParentId` + `virtual Parent Parent` on "many" side
- N-N: `virtual ICollection<T>` in **both** classes

### DbContext

```csharp
public class ClientManagerDbContext : DbContext
{
    public ClientManagerDbContext(DbContextOptions<ClientManagerDbContext> options) : base(options) { }

    public DbSet<Quiz> Quizzes { get; set; }   // one DbSet per entity
}
```

### Registration (Program.cs)

```csharp
builder.Services.AddDbContext<ClientManagerDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("ClientManagerDbContext"),
        opt => opt.MigrationsAssembly("Vjezba.DAL")));
```

### Connection string (appsettings.json)

```json
"ConnectionStrings": {
  "ClientManagerDbContext": "Data Source=127.0.0.1;Initial Catalog=mvc_2023_24;User ID=sa;Password=...;MultipleActiveResultSets=True;TrustServerCertificate=True;"
}
```

### Migrations

```powershell
# Add migration (from DAL project dir)
dotnet ef migrations add Initial --startup-project ../Vjezba.Web --context ClientManagerDbContext

# Apply to database
dotnet ef database update --startup-project ../Vjezba.Web --context ClientManagerDbContext

# Generate SQL script (for production)
dotnet ef migrations script FromMigration ToMigration
```

Workflow: change model → add migration → update database.

### Seed data

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);
    modelBuilder.Entity<City>().HasData(new City { ID = 1, Name = "Zagreb" });
}
```
Then generate + apply a new migration.

---

## CRUD with EF

### DI into controller (preferred)

```csharp
public class QuizController : Controller
{
    private QuizManagerDbContext _dbContext;

    public QuizController(QuizManagerDbContext dbContext)
    {
        _dbContext = dbContext;
    }
}
```

### Create

```csharp
_dbContext.Quizes.Add(quiz);
_dbContext.SaveChanges();
```

### Read (single)

```csharp
Quiz result = _dbContext.Quizes.Where(p => p.Id == id).FirstOrDefault();
Quiz result = _dbContext.Quizes.Find(id);
```

### Read (list + eager-load relations)

```csharp
List<Quiz> result = _dbContext.Quizes
    .Include(p => p.Category)          // eager-load navigation property
    .Where(p => p.DateCreated.Year == 2013)
    .ToList();                         // query executes here
```

- `Include()` required for accessing navigation properties; otherwise throws when accessing unloaded relation.
- `Include()` is in `Microsoft.EntityFrameworkCore` namespace.

### Update

```csharp
Quiz result = _dbContext.Quizes.Find(id);
result.Title = "New title";
_dbContext.SaveChanges();
```

### Delete

```csharp
Quiz result = _dbContext.Quizes.Find(id);
_dbContext.Entry(result).State = System.Data.Entity.EntityState.Deleted;
_dbContext.SaveChanges();
```
