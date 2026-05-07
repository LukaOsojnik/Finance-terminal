---
name: create-new-page
description: Guides creating a new MVC page (list or edit/create form) including controller action, route, ViewModel, and Razor view. Use when: adding a new page, new route, new list view, or new form to the application.
---

## Step 1: Read project context

Read `sitemap.md` first to understand all existing routes and avoid conflicts. Then read `semantic-model.md` to understand available entities and their fields.

If either file is missing, ask the user before proceeding.

## Step 2: Determine page type

Ask the user (or infer from context) whether this is:
- A **list page** — displays a collection of records
- An **edit/create form** — allows creating or editing a single record

## Step 3: Routing conventions

Follow the routing pattern already in use across the project:

- Class-level: `[Route("resource")]` — lowercase, plural noun matching the entity
- Index/list action: `[Route("")]` with `[HttpGet]`
- Details action: `[Route("{id:long}/meaningful-slug")]` — the slug must describe the page, not just say "details" (e.g., `/events/{id:long}/overview`, `/countries/{id:long}/profile`)
- Form GET: `[Route("new")]` or `[Route("{id:long}/edit")]`
- Form POST: same route as GET, with `[HttpPost]`

Read the existing controllers to confirm the exact pattern in use before writing any routes.

## Step 4a: List page implementation

**RowViewModel**

Create `Models/ViewModels/{Entity}RowViewModel.cs`:
- Properties map to what is displayed in the list row
- Include a `public static {Entity}RowViewModel From({Entity} e)` factory method that maps from the entity

**Controller action**

Add to the relevant controller (or create a new one):
```csharp
[HttpGet]
[Route("")]
public IActionResult Index()
{
    var items = _repository.GetAll();
    var viewModels = items.Select({Entity}RowViewModel.From);
    return View(viewModels);
}
```

Constructor injects only the repositories needed via `IXxxRepository` interfaces.

**Razor view**

Create `Views/{Controller}/Index.cshtml`:
```razor
@model IEnumerable<{Entity}RowViewModel>

<h1>...</h1>
<table>
  <thead>...</thead>
  <tbody>
    @foreach (var row in Model)
    {
      <tr>...</tr>
    }
  </tbody>
</table>
```

## Step 4b: Edit/create form implementation

**Form ViewModel**

Create `Models/ViewModels/{Entity}FormViewModel.cs`:
- Properties for each editable field
- Use `[Required]`, `[Display(Name = "...")]`, and other data annotations as appropriate

**Controller actions**

GET — load the form:
```csharp
[HttpGet]
[Route("new")]           // or [Route("{id:long}/edit")]
public IActionResult Create()   // or Edit(long id)
{
    var vm = new {Entity}FormViewModel();
    return View(vm);
}
```

POST — handle submission:
```csharp
[HttpPost]
[Route("new")]           // or [Route("{id:long}/edit")]
public IActionResult Create({Entity}FormViewModel vm)   // or Edit(long id, ...)
{
    if (!ModelState.IsValid)
        return View(vm);

    // map vm -> entity, call repository
    return RedirectToAction("Index");
}
```

**Razor view**

Create `Views/{Controller}/Create.cshtml` (or `Edit.cshtml`):
```razor
@model {Entity}FormViewModel

<h1>...</h1>
<form asp-action="Create" asp-controller="{Controller}" method="post">
    <div>
        <label asp-for="FieldName"></label>
        <input asp-for="FieldName" />
        <span asp-validation-for="FieldName"></span>
    </div>
    ...
    <button type="submit">Save</button>
</form>
```

Use ASP.NET tag helpers (`asp-for`, `asp-action`, `asp-controller`, `asp-validation-for`) throughout. Do not use `Html.` helper methods.

## Step 5: Constructor injection

Inject only the repositories actually needed by the new actions:

```csharp
private readonly IXxxRepository _repository;

public XxxController(IXxxRepository repository)
{
    _repository = repository;
}
```

Add to an existing controller constructor if the controller already exists. Never inject a repository that is not used.

## Step 6: Update sitemap

After the route is added and working, spawn a background agent to update `sitemap.md` with:
- The new URL pattern
- The controller name
- The action name
- The view path
- A one-line description of the page's purpose
