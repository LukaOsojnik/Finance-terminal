# Grill Me — Lab 4 Task 1

Self-quiz over every meaningful decision made implementing full CRUD + AJAX search.

---

## Soft delete

### Q1. Why `DateTime? DeletedAt` instead of `bool IsDeleted`?

Timestamp answers two questions in one column: *whether* deleted (`!= null`) and *when*. A boolean throws away the "when," which you need for audit, undo windows, GDPR retention, or simply sorting recent deletes. Storage cost identical for a nullable `DateTime` vs `bool` once the column is indexed for the common `WHERE DeletedAt IS NULL` predicate.

### Q2. Why filter `Where(e => e.DeletedAt == null)` in every repository method instead of EF Core's `HasQueryFilter`?

`HasQueryFilter` is the "right" answer long-term — set once on the model, applied to every query. I chose explicit filters because:
1. Visibility — anyone reading `CountryRepository.GetAll()` sees the soft-delete behavior without hunting through `OnModelCreating`.
2. Easy to opt out for an admin "show deleted" screen without `.IgnoreQueryFilters()` sprinkled around.
3. Lab 4 wants the pattern to be *taught*, not hidden.

The cost: every new repo method must remember the filter. Acceptable for 10 entities; would refactor to query filters past ~25.

### Q3. Why `SoftDelete` throw `InvalidOperationException` instead of returning a bool?

Throwing communicates *failure with a reason*. Bool returns force the controller to dig out the "why" separately, often via a second roundtrip or shared mutable state. The controller catches the exception and routes the message to `TempData["Error"]` → banner on Index. Pattern stays consistent across all 10 controllers.

### Q4. What happens if I delete a Country that still has live Companies?

`CountryRepository.SoftDelete(id)` checks `db.Companies.Any(x => x.CountryId == id && x.DeletedAt == null)`. If true, throws `InvalidOperationException("Cannot delete country: active companies exist.")`. The `CountriesController.Delete` action catches it, sets `TempData["Error"]` to the message, and redirects to Index where a red banner shows.

### Q5. Can I soft-delete an already-soft-deleted row?

No — `SoftDelete` first loads the entity, checks `if (entity == null || entity.DeletedAt != null) return;` and exits silently. Guards against double-clicks and concurrent deletes.

---

## Repository pattern

### Q6. Why `Add(T entity)` instead of taking the form model directly?

Separation. The repo speaks entities (domain). The controller speaks form models (HTTP boundary) and is responsible for the entity↔model translation. If the form model leaks into the repo, every form-shape change forces a repo change.

### Q7. Why does `EventRepository.Add` take three extra `IEnumerable<long>` params for the N:M ids?

Event has three many-to-many collections (Countries, Companies, TradeBlocs). The form posts checkbox arrays — `SelectedCountryIds` etc. The cleanest single-call API: hand the entity plus the three id lists. The repo internally hydrates `entity.Countries = db.Countries.Where(c => ids.Contains(c.Id) && c.DeletedAt == null)` then `SaveChanges`. EF diffs the join table for me.

### Q8. Why `EF.Functions.Like(name, $"%{t}%")` instead of `name.Contains(t)`?

Pomelo's MySQL provider translates both, but `Like` makes the SQL explicit (`LIKE '%term%'`) and lets me reason about index behavior. With `Contains`, the provider chooses; sometimes you get `LOCATE() > 0` which is index-unfriendly. Also `Like` plays nicely with case-insensitive collations (MySQL `utf8mb4_unicode_ci` defaults).

### Q9. Why does `GdpSnapshotRepository.Search` try `int.TryParse` on the term?

Search semantics differ by field: text fields match `Like`, numeric `Year` needs equality. If the user types `2024`, they want exact year matches; if `India`, country name `Like`. Branching on parse keeps both UX paths working from one input box.

### Q10. What's the constructor pattern `class CountryRepository(AppDbContext db) : ICountryRepository`?

C# 12 primary constructors. Saves the `private readonly AppDbContext _db; public CountryRepository(AppDbContext db) { _db = db; }` boilerplate. The parameter `db` is in scope across all instance members. Net effect identical to classic ctor injection; just terser.

---

## ViewModels

### Q11. Why `CountryEditModel : CountryCreateModel` (inheritance) but `CountryDetailsEditModel` standalone (no inheritance)?

Country edit needs every Create field + `Id`, so inheritance saves duplication. CountryDetails has `CountryId` as the *PK* — Create takes it from a dropdown (with validation that no details exist for that country yet); Edit treats it as immutable (you can't reassign details to a different country). Different responsibilities → different shapes → no inheritance.

### Q12. Why `[Range(-1, 1)]` on `CompanyEditModel.GrossMargin`?

Gross margin is a ratio (revenue − COGS) / revenue. Theoretical range −∞ to 1, but practical companies sit in (−1, 1). Wider range = noise; tighter rejects valid loss-making firms. The `[Range]` is also the seed for Lab4 task 3 client-side validation — same attribute drives jQuery unobtrusive validation in the browser.

### Q13. Why does `EventEditModel` carry `List<long> SelectedCountryIds` instead of `List<Country>`?

The form posts checkbox `value="@c.Id"` arrays. Model binder collects them as `List<long>`. Hydrating to `List<Country>` happens in the repo (`db.Countries.Where(c => ids.Contains(c.Id))`) — keeps DB access out of the binding layer and avoids round-tripping unused Country fields through the form.

### Q14. Why store ViewModels under `Models/ViewModels/` and not `ViewModels/` at the project root?

Convention from the existing project (CountryRowViewModel etc lived there). Keeping new ones beside the old keeps `@using simple_bloomberg_terminal.Models.ViewModels` working from `_ViewImports.cshtml` without changes.

---

## Controllers

### Q15. Why split `EditGet` and `EditPost` with `[ActionName("Edit")]` instead of one overloaded `Edit`?

C# method overloading goes by signature, not HTTP verb. Two methods `Edit(long id)` and `Edit(long id, EditModel m)` *could* coexist, but `Edit(long id)` alone (for the POST that re-fetches) can't. `[ActionName("Edit")]` decouples the C# method name (`EditGet`/`EditPost`) from the route (`Edit`), and `[HttpGet]`/`[HttpPost]` route to the right one. Lab 4 §"Atribut ActionName" explicitly teaches this.

### Q16. Why does `EditPost` re-fetch the entity instead of trusting the posted form?

Defense in depth. Posted form lacks fields the user can't edit (DeletedAt, CreatedAt, audit columns). If I bound the form straight onto an entity and called `Update`, EF would null those columns. Re-fetch → copy whitelisted fields → save. Lab 4 §"Problem zastarjelih podataka" mentions this too.

### Q17. Why `await TryUpdateModelAsync(model)` after manually constructing `model`?

Order matters:
1. Re-fetch entity from DB.
2. Build `model = ToEditModel(entity)` — model pre-populated with *current* DB values.
3. `TryUpdateModelAsync(model)` — overlays *posted* form values onto `model`, running validation as it goes.
4. If valid, copy `model` → entity via `ApplyEdit`.

Step 2 is the key: if validation fails after step 3, `View(model)` shows whatever the user typed (with their typos), not the DB values. UX win.

### Q18. Why `PopulateDropdowns()` as a private method instead of a filter or base class?

Plain method = grep-able, scope-limited, no inheritance magic. Lab 4 §556 directly says: *"preporučljivo je taj kod izdvojiti u posebnu funkciju."* I did exactly that. A `ResultFilter` would feel clever but obscure when dropdowns get populated. Private method calls are dumb and obvious.

### Q19. Why `[ValidateAntiForgeryToken]` on every POST?

Prevents CSRF. Without it, any site can POST a delete to `/countries/5/delete` with the user's cookies and the row dies. The token is per-form, in a hidden field rendered by `@Html.AntiForgeryToken()`, validated server-side per request. Lab 4 doesn't grade this but it's table stakes.

### Q20. Why does `CountryDetailsController.Create` check `_details.GetByCountryId(model.CountryId) != null` and add a `ModelState` error?

The schema has `[Key] long CountryId` — CountryDetails is 1:1 with Country, the PK *is* the FK. If I just `Add` a duplicate, MySQL throws on `SaveChanges` (PK violation), the user sees a generic 500. Pre-check + `ModelState.AddModelError` gives a friendly inline error: *"Country already has details (1:1)."* The Create dropdown also already filters out countries with details — belt and suspenders.

### Q21. Why does `EventsController` need `ICountryRepository`, `ICompanyRepository`, `ITradeBlocRepository` injected?

For the N:M checkbox lists on the Create/Edit forms. `PopulateDropdowns` calls each repo's `GetAll()` to render the available options. Without injection, the controller can't render the form.

---

## Routing

### Q22. Why slugify routes (`trade-blocs`, `country-details`) instead of `tradeblocs`?

Readability + SEO convention. Hyphenated kebab-case is the web standard for multi-word URL segments. `/country-advantages/5/edit` parses at a glance; `/countryadvantages/5/edit` requires squinting. C# names stay PascalCase (`TradeBlocsController`) — routing decouples them.

### Q23. Why `[Route("{id:long}/edit")]` instead of `[Route("edit/{id:long}")]`?

Convention I picked for the project — entity ID first, action second, matching `/{entity}/{id}/{action}`. Easier to read as "the country with id 5, edit page." Also avoids collision with reserved words: `/countries/edit` could be misread as "edit a country with name 'edit'" if you ever add slug-based routes.

### Q24. Why does `EventsController.Index` route as `[Route("feed")]` (so URL is `/events/feed`) while every other controller uses `[Route("")]`?

Legacy — that route existed before task 1, I preserved it per CLAUDE.md scope-control rule (don't change unrequested things). Costs: `/events` returns 404, nav link explicitly targets `Events/Index` which resolves through routing to `/events/feed`. Worth fixing in a follow-up — `[Route("")]` alias would normalize.

---

## Views

### Q25. Why `_TableBody.cshtml` partial instead of generating the `<tr>` markup twice (once for Index, once for Search)?

DRY at the view layer. Index calls `@await Html.PartialAsync("_TableBody", Model)` for initial render; Search action returns `PartialView("_TableBody", rows)` for AJAX. Same Razor, same HTML, identical rows in both paths. Diverging would mean a search update could subtly differ from the initial table.

### Q26. Why does `_TableBody` start with `@if (!Model.Any()) { <tr>No results</tr> }` and *not* `else`?

The empty-state row only renders when count is zero; the `foreach` below renders zero rows anyway. No `else` needed. The empty row provides UX feedback ("we did search, found nothing") instead of an awkward blank table body.

### Q27. Why `@Html.AntiForgeryToken()` inside each delete `<form>` rather than once at the top of Index?

Each `<form>` element is its own POST target. The antiforgery token belongs *inside* the form that submits it — the framework reads it from the form's payload, not from elsewhere on the page. One token outside the form = nothing for the framework to find.

### Q28. Why `onsubmit="return confirm('Delete @x?');"` inline instead of a JS handler in site.js?

Smallest possible footprint per Lab 4's spirit. The confirm prompt is per-row, per-name; binding it via JS would require data attributes, querySelector, event delegation — more code, harder to scan. Inline `confirm` returns false → form doesn't submit. Classic, ugly, works.

### Q29. Why does the Event form use `<input type="checkbox" name="SelectedCountryIds" value="@c.Id">` instead of `<select multiple>`?

Checkbox grid in a scrollable div is easier to scan with many countries. `<select multiple>` requires Ctrl+click on desktop and is broken on mobile. Both bind to `List<long>` the same way (the model binder collects multiple form values with the same name into a list). Checkbox = better UX, identical wire format.

### Q30. Why `@(Model.SelectedCountryIds.Contains(c.Id) ? "checked" : null)` instead of `@if`?

The Razor ternary into an HTML attribute renders `checked="checked"` when true and *nothing at all* when null. An `@if` would either render the entire `<input>` twice or require an awkward `@:checked` literal escape. The ternary keeps the markup symmetric.

---

## AJAX search

### Q31. Why 300ms debounce specifically?

It's the standard tradeoff:
- <200ms: feels instant but server fires on every keystroke.
- 200-300ms: still feels real-time, fires once per word.
- 300-500ms: noticeable pause, fires less often.
- >500ms: lag.

300ms hits the sweet spot for a typed-and-paused search.

### Q32. Why `tbody.innerHTML = html` instead of building DOM nodes from JSON?

Razor renders the row markup server-side, including escaping, formatting (currency, percentages, dates), risk-class CSS classes. Re-implementing all of that in JS doubles the work and risks divergence. HTML over the wire = single source of truth, slightly more bytes, simpler client.

### Q33. Why does `.catch(() => { /* keep prior rows */ })` deliberately swallow errors?

A transient 500 or dropped WiFi shouldn't wipe the user's visible data. Stale rows are less alarming than an empty table mid-typing. The user retries with one more keystroke, and the next fetch refreshes. Production code would also surface a toast; task 1 keeps it minimal.

### Q34. What if the search returns thousands of rows?

Today: the full tbody re-renders, browser handles it. Acceptable for the data sizes in this app (~10s-100s of rows). Above ~10k rows, you'd add server-side pagination + a "load more" sentinel, or switch to a virtualized table. Lab 4 §"Performanse autocomplete pretrage" gives the same advice.

### Q35. Why `data-search-url="@Url.Action("Search")"` instead of hardcoding the URL?

`Url.Action` respects the controller's `[Route]` attribute. If I rename the route, the form's URL updates automatically — no string-search-and-replace through views. Also robust to running the app under a non-root pathbase (`/myapp/countries/search`).

---

## Database

### Q36. Why MySQL `datetime(6)` for `DeletedAt`?

Pomelo's default mapping for `DateTime?`. The `(6)` gives microsecond precision (6 fractional digits) — overkill for soft-delete but free. The migration generates `ALTER TABLE … ADD DeletedAt datetime(6) NULL`. Nullable in SQL because the row is "live" until set.

### Q37. Why doesn't the migration add an index on `DeletedAt`?

EF Core won't auto-index a column just for nullability. Every list query filters `WHERE DeletedAt IS NULL`, so an index would help — *but* in practice the query also filters by other criteria (id, name search), and MySQL's optimizer usually picks the more selective index. For 10k+ rows per table I'd add `CREATE INDEX ix_x_deleted_at ON X(DeletedAt)`. Skipped for now; YAGNI for the lab's data volume.

### Q38. Could two users delete the same row simultaneously?

Yes, but harmless. Both `SoftDelete` calls re-load the entity, the first sets `DeletedAt = UtcNow` and saves, the second finds `entity.DeletedAt != null` and exits silently. No data corruption. EF's optimistic concurrency would also flag the conflict if I added a `[ConcurrencyCheck]` column — not needed here.

---

## What's missing on purpose

### Q39. Why no validation attributes beyond `[Required]`/`[Range]`/`[StringLength]`?

Lab 4 task 3 is dedicated to validation (client + server, message UX). The basic attrs are here to enable `ModelState.IsValid` for the CRUD POST path. Task 3 will add custom validators, cross-field rules, jQuery unobtrusive wiring, localized messages, styling.

### Q40. Why no autocomplete on the FK dropdowns?

Lab 4 task 2 is exactly that. Today the dropdowns are static `<select asp-items>` populated by `PopulateDropdowns()`. Task 2 will swap them for a custom kontrola hitting a `Search` endpoint via AJAX, rendering matches in a popup. Server-side `Search` actions already exist (built for task 1's tbody refresh) — same endpoints serve autocomplete with minor reshape.

---

# Task 2 — Autocomplete Dropdown

## Lookup endpoints

### Q41. Why a separate `Lookup` action instead of reusing `Search`?

`Search` returns `_TableBody` partial HTML (designed to swap into `<tbody id="table-body">`). The autocomplete needs JSON `[{id, label}]` to build `<li>` rows in the dropdown popup. Different shape, different content type. Reusing would force one endpoint to switch return type based on `Accept` header — extra branching, harder to debug. Two thin actions sharing the same `_repo.Search(term)` call is cleaner: 3 lines each.

### Q42. Why `Take(10)`?

UX bound. A dropdown with 200 results is unusable — user can't scroll-skim. 10 fits in the 240px max-height without scrolling, and forces the user to refine the query. Also bounds the JSON payload (~400 bytes for 10 rows vs ~8kB for 200). The lab notes "filtering + Take(10-20)" as the canonical autocomplete pattern.

### Q43. Why filter `term` server-side instead of pulling all rows and filtering in JS?

Two reasons:
1. **Bandwidth** — 10 rows × 50 bytes = 500B per keystroke vs ~50kB for a full Countries dump (with `EagerLoad` of Region etc).
2. **Authoritative truth** — server enforces `DeletedAt == null` filter. Soft-deleted countries must never appear in a picker (otherwise users select a stale FK that fails on save). Client-side filter can't be trusted.

The hybrid approach (load all + filter client-side) is what the lab calls "for small option sets, no pretraga needed" — fine for enums, wrong for Country/Company.

## The picker partial

### Q44. Why a `<input type="hidden">` next to the visible text input?

Two separable concerns: what the user *sees* (the country name "Germany") vs what the form *posts* (the FK id `2`). The visible input is for typing/searching — its value is never posted (no `name=` attr). The hidden carries `name="CountryId"` so the model binder hydrates `model.CountryId = 2L`. Pick action sets hidden value; type action clears it (so an unconfirmed typed-but-not-picked text doesn't accidentally post a stale id).

### Q45. Why `data-val-*` on the hidden input — why not `asp-for`?

`asp-for="CountryId"` would let the Tag Helper auto-emit `data-val-required`, `data-val-remote-*`, etc. from the model's DataAnnotations. But it would also generate the wrong input type (text, not hidden) and bind to the wrong DOM element. Hand-emitting `data-val-*` in the partial lets the picker stay model-agnostic — the same partial works for `CountryId`, `CompanyId`, `RelatedCompanyId` without knowing the parent ViewModel's shape. Trade-off: I lose Tag Helper auto-discovery and have to pass `Required`/`RemoteUrl` explicitly through `AutocompletePickerModel`.

### Q46. Why is `AutocompletePickerModel.RemoteUrl` nullable?

Most fields don't need a Remote uniqueness check (e.g. Company.CountryId — any country is fine). Setting `RemoteUrl = null` makes the partial skip emitting the four `data-val-remote-*` attrs entirely. Only `CountryDetailsCreateModel.CountryId` (1:1 constraint) opts in. Avoids dead attrs and wasted XHR pings on unrelated forms.

## Client JS

### Q47. Why 250ms debounce?

Bound between "responsive" and "wasteful". 100ms is too tight — pressing "Ger" issues three lookups before the user even finishes typing. 500ms feels laggy. 250ms is the conventional autocomplete sweet spot: a fast typist completes "Ger" in ~200ms, the lookup fires once after they pause.

### Q48. Why does typing clear the hidden field?

Defensive. After picking "Germany" (hidden = 2), if the user starts re-typing "Fr" to switch to France, the visible input now reads "Fr" but the hidden would still hold `2`. Submitting at that moment would silently post the old id — visual/posted-value mismatch, classic UX bug. Clearing hidden on every `input` event forces the user to pick again explicitly, or fail validation.

### Q49. Why `mousedown` on the list, not `click`?

`blur` on the input fires *before* `click` on the list item (mouse-down → input blur → mouse-up → click). With `click`, the input's `blur` handler hides the list before `click` ever fires — clicking does nothing. `mousedown` fires *before* `blur`, so the pick handler runs first. The `setTimeout(hide, 150)` on blur also guards against the race.

### Q50. Why dispatch `Event('blur')` on the hidden field after picking?

jquery-unobtrusive validates fields on `blur`. The hidden field never fires `blur` naturally (it's not focusable). After `pick()` sets its value, manually firing blur kicks the `dategte`/`remote`/`required` validators to re-evaluate. Otherwise the "Please select a value." error stays visible even after the user successfully picked.

### Q51. Why keyboard nav (↑/↓/Enter/Esc)?

A11y + speed. Mouse-only autocomplete is unfriendly to keyboard users and slow. Arrow keys highlight rows (CSS `.active` class), Enter picks, Esc closes without picking. Cyclic wrap (`(i+1) % n`) avoids dead-end behaviour at list boundaries.

## Controller changes

### Q52. Why drop `ViewBag.Countries = new SelectList(...)` from `PopulateDropdowns()`?

The static `<select asp-items="ViewBag.Countries">` is gone. Building the SelectList would load every Country (or Company) row from DB on every form open just to throw it away. Net win: form GET no longer N+1's the full table — picker only fetches the ≤10 rows the user actually searches for.

### Q53. Why `ViewBag.CountryLabel` (string) on Edit but not on Create?

Edit must show the existing FK's display name preselected ("Germany" in the input box). The picker's `SelectedLabel` parameter reads from `ViewBag.CountryLabel`. Create has no preselected value, so it's left empty. Source: `entity.Country?.Name` — the navigation is already `.Include()`d in `_repo.GetById()` (verified across all 7 child repos), so no extra DB hit.

### Q54. What happens on POST-fail re-render if user changed the picker?

Edge case: user picks "France" (hidden = 5), submits, server-side validation fails on a different field. The view re-renders with `model.CountryId = 5` (correct) but `ViewBag.CountryLabel = entity.Country?.Name = "Germany"` (the original entity). Visible text shows "Germany"; hidden carries `5`. Mismatch.

Acceptable trade-off for lab scope. Cleaner fix: look up `_countries.GetById(model.CountryId)?.Name` on POST fail. Not done — keeps controllers simple, and validation failure is uncommon enough.

## Scope choices

### Q55. Why aren't `Sector`, `EventType`, `CostBase` etc. swapped to autocomplete?

Lab explicitly: *"Statički dropdown za < 20 opcija."* Enums have ~5-15 values. Loading all at form-open is cheap. Autocomplete adds latency (one XHR per keystroke) and complexity for zero UX gain — the user can scan a 10-item `<select>` faster than they can type. Picker reserved for high-cardinality (Country: 100+, Company: 1000s).

### Q56. Why keep the `Event` checkbox multi-select for N:M instead of multi-autocomplete?

Multi-select autocomplete (picking multiple chips) is a different control entirely — needs chip rendering, remove-X buttons, picked-vs-unpicked exclusion in lookup results. Out of scope for task 2 which says "dropdown s autocomplete pretragom" (singular). Checkboxes work for the lab's small N:M sets (countries linked to an event = ~10 max).

---

# Task 3 — Validation

## Wiring

### Q57. Why didn't `_Layout.cshtml` need changes for validation?

Already correct from task 1:
```
jquery.min.js → bootstrap.bundle.min.js → site.js → @RenderSectionAsync("Scripts")
```
Each form already had `@section Scripts { <partial name="_ValidationScriptsPartial"/> }`. That partial loads `jquery.validate.min.js` + `jquery.validate.unobtrusive.min.js`. The DataAnnotations on ViewModels (`[Required]`, `[Range]`, `[StringLength]`) were already there. Task 3 just polished CSS and added two custom rules.

### Q58. Why is the `dategte` jQuery adapter inside `_ValidationScriptsPartial.cshtml` instead of `site.js`?

Load order. `site.js` runs *before* `_ValidationScriptsPartial` per the layout — so `jQuery.validator.addMethod()` would fail (validator not loaded yet). The partial loads validators then runs the adapter registration script inline, guaranteed correct order on every page.

## Custom date attribute

### Q59. Why a custom `DateGreaterThanOrEqualAttribute` instead of built-in `[Compare]`?

`[Compare("Date")]` only checks string equality (used for "confirm password" scenarios). Doesn't understand `≥`, doesn't handle date comparison semantics, doesn't know about `DateOnly`. Custom attribute lets me cast to `DateOnly`/`DateTime` correctly and define the comparison rule precisely.

### Q60. Why handle both `DateOnly` and `DateTime` in `IsValid`?

Forward-compatibility. Event uses `DateOnly` (no time component — events are date-based). Other future entities might use `DateTime`. One attribute, two type branches → reusable across any date-typed field without per-type attributes. Minor code, large flexibility win.

### Q61. Why `IClientModelValidator` on the attribute?

Without it, the attribute only fires on server-side `ModelState.IsValid`. `AddValidation()` emits `data-val-dategte` + `data-val-dategte-other` HTML attrs at render time. jquery-unobtrusive's adapter (`addSingleVal('dategte', 'other')`) picks them up and ties them to the JS rule registered in `_ValidationScriptsPartial`. Result: same rule fires on blur client-side *and* on POST server-side, identical message.

### Q62. Why `addSingleVal('dategte', 'other')` not `add()`?

`addSingleVal` is sugar for "this rule takes exactly one parameter from `data-val-X-{paramName}`". `add()` is the general form requiring you to spell out the adapter callback. For a one-param rule (the other field's name), `addSingleVal` is two lines instead of seven.

## [Remote] uniqueness

### Q63. Why `[Remote]` on `CountryDetailsCreateModel.CountryId` if there's already a server check?

`[Remote]` runs as the user fills the form (on blur of CountryId), via AJAX to `/country-details/validate-country?countryId=N`. User sees "This country already has details." *before* clicking submit — no full POST cycle. Server-side pre-check in `Create` POST remains because client-side validation is bypassable (curl, DOM tampering, JS disabled).

### Q64. Why does `ValidateCountry` return `Json(bool)` not an error message?

jquery-unobtrusive's Remote contract: server returns `true` (valid) or `false` (invalid). The error message is configured client-side via `data-val-remote="error string"` from the `[Remote]` attribute's `ErrorMessage`. Keeps server logic dead simple — one boolean.

### Q65. Why did the picker's hidden input not auto-emit the `[Remote]` attrs?

The Tag Helper that emits `data-val-remote-*` from `[Remote]` runs on inputs created via `asp-for="CountryId"`. My picker partial uses a raw `<input type="hidden">` (no `asp-for`) so the Tag Helper never sees it. Workaround: I added `RemoteUrl` to `AutocompletePickerModel` and hand-emit the four `data-val-remote-*` attrs in the partial. The CountryDetails/Create view explicitly passes `RemoteUrl = Url.Action("ValidateCountry", "CountryDetails")`.

### Q66. Why `data-val-remote-additionalfields="*.CountryId"`?

The `*.` prefix is jquery-unobtrusive convention meaning "this form's instance of the field". Tells the validator to send the current value of the picker's hidden input as the `countryId` query param. Server then receives `?countryId=5` and looks up `_details.GetByCountryId(5)`. Without this, no params would be sent and `countryId` would default to 0 server-side — false-positive "available".

## Styling

### Q67. Why override `.text-danger` instead of using Bootstrap's default?

Bootstrap red doesn't fit the terminal theme. Custom CSS uses `var(--red)` (#f85149) + `var(--font-mono)` + tight letter-spacing. Sits flush under the input (`margin-top: 0.25rem`). Same hue used for the `.input-validation-error` border and the `.validation-summary-errors` box → consistent error language across the UI.

### Q68. Why `box-shadow: 0 0 0 2px #f8514933` on invalid inputs?

Mimics the terminal's existing focus glow (`var(--accent-glow)`) but in red. Reinforces "this field is wrong" visually beyond just a border-color change — color-blind users notice the glow even if red/green look similar. Inherits the same design vocabulary as the autocomplete dropdown's accent glow.

### Q69. Why is the `.validation-summary-errors` box conditional on the `<div asp-validation-summary="ModelOnly">`?

`ModelOnly` shows only errors *not* tied to a specific field (e.g. business-rule errors from `ModelState.AddModelError("", "...")`). Per-field errors render under each input via `<span asp-validation-for>`. Splitting them avoids double-display — same error showing in the summary AND under the field. The summary box appears only when there's something to summarize.

## What's still missing

### Q70. Why no `[EmailAddress]`, `[RegularExpression]`, or `[Phone]`?

No model field needs them. The CRUD entities are countries/companies/events — no email, phone, or formatted-string columns. If a future feature added a user contact form, those attrs would slot in with zero infrastructure work (libs and CSS already cover them).

### Q71. Why no localization of error messages?

Lab 4's task 3 doesn't explicitly require localization. Messages are English strings hardcoded in attributes (`ErrorMessage = "..."`). Localizing would mean `IStringLocalizer` injection in models, `.resx` files, and `UseRequestLocalization()` middleware — that infra is task 5's territory (datepicker culture handling). Deferred.

### Q72. Could a user bypass the `[Remote]` check by racing?

Yes. TOCTOU: user A and user B both check `/validate-country?countryId=5` simultaneously, both get `true`, both POST. Server-side pre-check in `Create` POST catches it for the second commit (returns 200 with ModelState error) — the unique-FK enforcement at the DB level (CountryDetails PK = CountryId) is the ultimate backstop. The [Remote] check is UX only.
