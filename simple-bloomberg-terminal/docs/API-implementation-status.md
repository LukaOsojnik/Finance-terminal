# API Implementation Status

Handoff context for continuing the REST API + integration-test work. Read
`API-convention.md` first for the design rules; this file records **what is actually
built and verified** as of now.

## Scope of this work

Assignment task = complete CRUD + DTO API for all entities **+ integration tests**.
SEC EDGAR `StockService` is intentionally **out of scope** (not built).

Confirmed decisions:
- **Mapping:** AutoMapper (package `AutoMapper` **16.1.1**). One `MappingProfile.cs`.
- **Test DB:** SQLite in-memory via `WebApplicationFactory` (real SQL, so repo
  `EF.Functions.Like` searches translate).

## DONE — API layer (built, builds clean, smoke-verified)

### Files added (main project `simple-bloomberg-terminal/`)
- `Dtos/` — one file per entity + `RelatedRefDto.cs`:
  - Response DTOs are positional `record`s (no `DeletedAt`, no raw audit fields).
  - Request DTOs are `record`s with `{ get; init; }` props + `[Required]`/`[Range]`
    annotations (drives `[ApiController]` auto-400). **One `XxxRequestDto` per entity,
    reused for both POST and PUT.**
  - Nested DTOs: `CompanyDto` nests `CountryDto` + `List<RevenueSourceDto>` +
    `List<CostSourceDto>`; `EventDto`/`TradeBlocDto` nest `List<RelatedRefDto>` (Id+Name).
- `MappingProfile.cs` — all entity↔DTO maps incl. `Event` (M:N collections → `RelatedRefDto`;
  write-side nav collections `.Ignore()`d because repos apply join membership from id lists).
- `Controllers/Api/` — 10 controllers, all `[ApiController]` + `[Route("api/[controller]")]`,
  inject the existing repository + `IMapper` (no `AppDbContext`).

### Files modified (main project)
- `Program.cs`:
  - `builder.Services.AddAutoMapper(cfg => { }, typeof(Program).Assembly);` (v16 API).
  - `public partial class Program { }` appended (so `WebApplicationFactory<Program>` works).
  - **Testing-env guard** around `ServerVersion.AutoDetect(...)`: under environment
    `"Testing"` it uses a fixed `MySqlServerVersion(8,0,0)` instead of connecting to MySQL
    at startup. Required because AutoDetect opens a live MySQL connection during host build.
- `simple-bloomberg-terminal.csproj`: added `AutoMapper` 16.1.1 PackageReference.

### Endpoint matrix (all 10 entities)
`GET /api/{controller}?q=` (search via repo `Search`, else `GetAll`) ·
`GET /api/{controller}/{id}` · `POST` · `PUT /{id}` · `DELETE /{id}` (soft delete).

Controller route tokens (case-insensitive): `countries`, `companies`, `events`,
`tradeblocs`, `countrydetails`, `countryadvantages`, `countrychallenges`,
`gdpsnapshots`, `revenuesources`, `costsources`.

### Per-controller deviations from the uniform pattern
- **CompaniesController:** `GetById`/`Create`/`Update` use `GetWithGraphRelations` so nested
  revenue/cost sources populate. `Delete` catches `InvalidOperationException` → **409**
  (active revenue/cost sources block soft-delete).
- **EventsController:** POST/PUT call the id-list repo overloads
  `Add/Update(entity, countryIds, companyIds, tradeBlocIds)`. No business-rule 409.
- **TradeBlocsController:** POST/PUT call `Add/Update(entity, countryIds)`. `Delete` →
  **409** when active member countries exist (repo throws).
- **CountryDetailsController:** keyed by `{countryId:long}` (1:1 with Country, no own Id);
  uses `GetById(countryId)` / `SoftDelete(countryId)`.
- The other 6 (Countries, CountryAdvantages, CountryChallenges, GdpSnapshots,
  RevenueSources, CostSources) are the plain uniform pattern.

## DONE — test infrastructure (project `simple-bloomberg-terminal.Tests/`)

Project at git root, sibling of the web project (NOT nested — the Web SDK globs all `.cs`),
added to `simple-bloomberg-terminal.sln`. Target `net10.0`.

Packages: `Microsoft.NET.Test.Sdk` 17.14.1, `xunit` 2.9.3, `xunit.runner.visualstudio`
3.1.4, `Microsoft.AspNetCore.Mvc.Testing` 10.0.8, `Microsoft.EntityFrameworkCore.Sqlite`
9.0.0. ProjectReference → web project.

- **`CustomWebApplicationFactory.cs`** — `WebApplicationFactory<Program>`:
  - sets env `"Testing"`, removes the MySQL `AppDbContext`/options descriptors, registers
    `AppDbContext` on a single **kept-open** `SqliteConnection("DataSource=:memory:")`.
  - `CreateHost` override: `EnsureCreated()` + `Seed(db)`.
  - Each factory instance = its own in-memory DB. **Seeded ids exposed as `const`s.**
- **`ApiTestBase.cs`** — base class; xUnit builds a new test-class instance per test method,
  so each test gets a fresh factory → fresh seeded DB → **full isolation** even with
  mutating tests. `Factory` + `Client` provided; `IDisposable`.
- **`SmokeTests.cs`** — 11 cross-cutting checks (see "Verified" below). Treat as
  representative/throwaway; decide whether to keep or fold into per-controller suites.

### Seeded fixture (what tests may assume — see factory constants)
| Entity | Id(s) | Notes |
|---|---|---|
| Country | `CountryUsId=1` (US), `CountryDeId=2` (DE) | |
| Company | `CompanyDeletableId=1` (Apple, no sources) | DELETE → 204 |
| Company | `CompanyBlockedId=2` (Microsoft, has RevenueSource) | DELETE → 409 |
| RevenueSource | `RevenueSourceId=1` (on Microsoft) | |
| Event | `EventId=1` | linked to US + Apple (M:N) |
| TradeBloc | `TradeBlocDeletableId=1` (EU, no members) | DELETE → 204 |
| TradeBloc | `TradeBlocBlockedId=2` (NAFTA, member US) | DELETE → 409 |
| CountryDetails | CountryId=1 | "Global leader" |
| CountryAdvantage | `CountryAdvantageId=1` | on US |
| CountryChallenge | `CountryChallengeId=1` | on US |
| GdpSnapshot | `GdpSnapshotId=1` | US, 2023 |
| (missing) | `MissingId=999999` | never seeded — use for 404 tests |

## Verified (via `dotnet test`, 98/98 passing)

Routing, AutoMapper runtime config, nested DTOs, `deletedAt` NOT leaked in JSON, `?q=`
LIKE search filtering, 404 on missing id, 400 on missing `[Required]`, 400 on `[Range]`,
409 on Company-with-revenue delete and TradeBloc-with-member delete, Event M:N nested output,
soft-deleted rows excluded from GET (DELETE → 204 → subsequent GET 404).

`dotnet test` builds both projects clean (0 warnings/errors).

> Note: local MySQL (localhost:3306) is **down**, so the real app cannot be run directly;
> all validation went through the SQLite test host. This does not affect production wiring.

## DONE — full per-controller integration suite

One test class per controller, all extend `ApiTestBase`, all use the seeded `const` ids.
`SmokeTests.cs` removed (folded into the per-controller suites). **98 tests, all passing.**

| Suite | Tests | Notes |
|---|---|---|
| `CompanyTests` | 12 | nested Country + RevenueSources; 409 on delete-with-revenue |
| `EventTests` | 10 | M:N id-lists; PUT replaces membership; no 409 |
| `TradeBlocTests` | 11 | id-list members; 409 on delete-with-member |
| `CountryDetailsTests` | 9 | keyed by `countryId` (1:1); Germany (id 2) used for Create |
| `CountryTests` | 10 | create-then-delete (US has FK children) |
| `CountryAdvantageTests` | 9 | plain CRUD |
| `CountryChallengeTests` | 9 | plain CRUD |
| `GdpSnapshotTests` | 10 | + `[Range]` Year out-of-range 400 case |
| `RevenueSourceTests` | 9 | plain CRUD |
| `CostSourceTests` | 9 | self-seeds (no fixture row); helper `CreateCostSourceAsync` |

Per entity: GetAll (+`?q=` where searchable), GetById ok/404, Create 201 (+Location)/400,
Update 200/404, Delete 204 → then GetById 404, Delete-missing 404. Plus the 409 cases.
Assertion style = status code **+ body fields** (per user). Fresh-DB-per-test gives isolation,
so mutating tests need no cleanup.

### App fix made during testing — `[Required]` on value-type FKs
The GdpSnapshot suite exposed that `[Required]` on a non-nullable `long` is a **no-op**: an
omitted FK binds to `0`, passes validation, and 500s on the DB FK instead of returning 400.
Fixed by making required FKs nullable in the request DTOs: `[Required] public long? CountryId`
(and `CompanyId`). Entity fields stay non-nullable `long`; AutoMapper unwraps `long?` → `long`.
Touched DTOs: Company, GdpSnapshot, CountryAdvantage, CountryChallenge, CountryDetails,
RevenueSource, CostSource. Still-open edge: an explicit `0` passes presence validation and
500s on the FK — would need a controller-level existence check (not done; different concern).

