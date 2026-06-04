# Implementation Plan — extraction pipeline + external APIs

Handoff for a fresh-context agent. Execute steps **in order**. Each step lists files, conventions, and an acceptance check. Read the linked design docs before coding.

## Read first
- `docs/extraction-pipeline.md` — the two-phase pipeline + the `SourceFieldReview` table (the thing being built).
- `docs/api-model.md` — sourcing the company-relationship (`RelatedCompanyId`) graph.
- `docs/external-api.md` — catalog of external APIs and what each feeds.
- `docs/API-convention.md` — **the law** for anything under `Controllers/Api/`, `Dtos/`, `Services/`. EDGAR is fully spec'd in its §9.
- `docs/semantic-model.md` — current DB model.
- `CLAUDE.md` (project) — conventions: no abstraction without demonstrated need; ask 1-3 foundation questions before non-trivial work; implement only what's requested.

## Current built state (do not redo)
- REST API (CRUD + DTOs) for all 10 entities is **done and tested** (98 passing). AutoMapper 16.1.1, one `MappingProfile.cs`. Tests in `simple-bloomberg-terminal.Tests/` run on SQLite in-memory via `CustomWebApplicationFactory`.
- **EDGAR `StockService` is NOT built** (was out of scope). Spec lives in `API-convention.md` §9.
- Source rows (`RevenueSource`/`CostSource`) today come only from manual MVC CRUD.

## Environment gotchas
- Real DB = **MySQL** (Pomelo). Local MySQL is often **down** → the running app can't connect, but `dotnet ef migrations add` works offline (model-based) and **tests use SQLite**, so verify via `dotnet test`, not the live app.
- DB tables are NOT created by migrations in tests — the test host calls `EnsureCreated()`, so a new `DbSet` is picked up automatically.
- Per-repo soft-delete: every read filters `Where(e => e.DeletedAt == null)`. There is no global query filter — match the existing repos.
- `[Required]` on a non-nullable `long` FK is a **no-op** (binds to 0, 500s on DB). In request DTOs make required FKs `long?`. (See `API-implementation-status.md` "App fix".)

---

## STEP 1a — `SourceFieldReview` data layer (no dependencies — start here)

Build the single table that holds per-field provenance **and** the AI verdict.

### Enums (`Models/Enums/`)
```csharp
public enum RelationKind { COST, REVENUE }
public enum ReviewableField { VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION }
```

### Entity (`Models/Entities/SourceFieldReview.cs`)
| Property | Type | Notes |
|---|---|---|
| `Id` | `long` | PK |
| `CompanyId` | `long` | FK→Companies — analyzed company (denormalized for "all reviews for X") |
| `Relation` | `RelationKind` | discriminator |
| `RevenueSourceId` | `long?` | FK→RevenueSources; set iff `Relation==REVENUE` |
| `CostSourceId` | `long?` | FK→CostSources; set iff `Relation==COST` |
| `Field` | `ReviewableField` | which cell this row proves |
| `Endpoint` | `string` | which API endpoint produced the proof |
| `ReferencePointer` | `string` | JSON path / text offset the user selected |
| `ReferenceSnapshot` | `string` | literal proof text, frozen at reference time |
| `ReferencedValue` | `string?` | value snapshot at reference time → staleness detection |
| `Mark` | `int?` | null=unreviewed, 0=fail, 1=pass |
| `Rationale` | `string?` | AI reason for the mark |
| `ReviewedAt` | `DateTime?` | |
| `ReviewerModel` | `string?` | model id |
| `DeletedAt` | `DateTime?` | soft-delete convention |

Add nav props matching existing entity style (`Company? Company`, `RevenueSource? RevenueSource`, `CostSource? CostSource`).

### `Data/AppDbContext.cs`
- `DbSet<SourceFieldReview>`.
- Configure the three FKs with `DeleteBehavior.Restrict` (MySQL rejects multiple cascade paths — match how existing relations are configured; read the file first).
- **Two unique indexes** for "one current reference per cell":
  `HasIndex(x => new { x.RevenueSourceId, x.Field }).IsUnique()` and
  `HasIndex(x => new { x.CostSourceId, x.Field }).IsUnique()`.
  MySQL allows multiple NULLs in a unique index, so cost rows (null `RevenueSourceId`) and revenue rows (null `CostSourceId`) don't collide — no filtered index needed.
- Check constraint "exactly one FK set":
  `t.HasCheckConstraint("CK_SourceFieldReview_OneSource", "(RevenueSourceId IS NULL) <> (CostSourceId IS NULL)")`.

### Repository (`Repositories/ISourceFieldReviewRepository.cs` + impl)
Mirror the existing repo shape (constructor injects `AppDbContext`, every read filters `DeletedAt == null`):
```csharp
IEnumerable<SourceFieldReview> GetByCompany(long companyId);
IEnumerable<SourceFieldReview> GetUnreviewed();            // Mark == null, for phase 2
SourceFieldReview? GetById(long id);
void Add(SourceFieldReview entity);
void Update(SourceFieldReview entity);
void SoftDelete(long id);
```
Register in `Program.cs`: `builder.Services.AddScoped<ISourceFieldReviewRepository, SourceFieldReviewRepository>();`

### Migration
`dotnet ef migrations add AddSourceFieldReview` (works offline). Do not run `database update` if MySQL is down.

### After this step (project CLAUDE.md rule)
Creating an EF entity + editing `AppDbContext` → **spawn a background agent to update `docs/semantic-model.md`** (add the table, columns, FKs, enums, the 1:N from RevenueSource/CostSource).

### Acceptance
`dotnet test` builds clean and stays green (new entity picked up by `EnsureCreated`); migration file generated.

### ✅ DONE — 2026-05-29 (completion report)
Step 1a is complete and merged into the working tree. What shipped:

**Files added**
- `Models/Enums/RelationKind.cs` — `{ COST, REVENUE }`
- `Models/Enums/ReviewableField.cs` — `{ VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION }`
- `Models/Entities/SourceFieldReview.cs` — all 15 columns per the spec table; three nav props (`Company?`, `RevenueSource?`, `CostSource?`) via `[ForeignKey]` attributes matching `RevenueSource.cs` style.
- `Repositories/ISourceFieldReviewRepository.cs` + `SourceFieldReviewRepository.cs` — primary-constructor style `(AppDbContext db)`; every read filters `DeletedAt == null`; `GetByCompany`, `GetUnreviewed` (`Mark == null`), `GetById`, `Add`, `Update`, `SoftDelete`. All reads `.Include` the three navs.
- `Migrations/20260529205842_AddSourceFieldReview.cs` (+ Designer) — generated offline; **`database update` NOT run** (MySQL was down).

**Files edited**
- `Data/AppDbContext.cs` — added `DbSet<SourceFieldReview>`; `OnModelCreating` block: all three FKs `OnDelete(DeleteBehavior.Restrict)`, two unique indexes `(RevenueSourceId, Field)` / `(CostSourceId, Field)`, check constraint `CK_SourceFieldReview_OneSource = "(RevenueSourceId IS NULL) <> (CostSourceId IS NULL)"` via the EF8+ `ToTable(t => t.HasCheckConstraint(...))` form.
- `Program.cs` — `AddScoped<ISourceFieldReviewRepository, SourceFieldReviewRepository>()`.
- `docs/semantic-model.md` — added table row, full `SourceFieldReview` model section (columns + constraints note), three 1:N relationships, and the two new enums.

**Solved / gotchas hit**
- Non-nullable string props (`Endpoint`, `ReferencePointer`, `ReferenceSnapshot`) → initialized `= string.Empty` to clear CS8618 (entity has no constructor, unlike `RevenueSource`).
- `dotnet ef` is a **global** tool (10.0.8) but not on the Bash-tool PATH → invoke via `& "$env:USERPROFILE\.dotnet\tools\dotnet-ef.exe"` from PowerShell.
- `dotnet test` must target the **sibling** test project `../simple-bloomberg-terminal.Tests/simple-bloomberg-terminal.Tests.csproj` — running it from the app project dir silently does nothing (no tests there, no .sln).
- The running app locks `bin/Debug/.../simple-bloomberg-terminal.exe` and blocks `build`/`ef` (MSB3027). Stop the app process first.

**Verification:** `dotnet test` → **103 passed, 0 failed**. New entity auto-picked-up by `EnsureCreated`; check constraint + unique indexes + Restrict FKs all hold on SQLite.

**Stopped here.** Nothing partial left open in 1a. Next: STEP 2 (EDGAR) or STEP 4 (reviewer) — both unblocked.

---

## STEP 2 — EDGAR `StockService` (first real endpoint + first source data)

Build exactly to `API-convention.md` §9 (it's fully spec'd: `IStockApiClient`/`StockApiClient`, `StockService`, the EDGAR→entity mapping table, the idempotent `POST /api/stock/refresh/{companyId}` flow, failure codes, the new repo "clear by company + DataSource" methods).

**Why before the UI:** the phase-1 UI's right pane renders an API response; EDGAR is the first browsable endpoint, and its output (`RevenueSource`/`CostSource` tagged `DataSource.EDGAR`) is precisely what the pipeline reviews. (World Bank/Finnhub from `external-api.md` are later — they feed the macro/non-US sides, not cost/revenue first.)

### Acceptance
`POST /api/stock/refresh/{companyId}` on a seed company with a `Cik` (e.g. Apple) persists EDGAR-tagged `RevenueSource`/`CostSource` rows; idempotent re-run doesn't duplicate; non-filer (null `Cik`) → 409. Verify via `dotnet test` (add a test or use the SQLite host).

### ✅ DONE — 2026-05-29 (completion report)
Step 2 complete and green. Built exactly to §9; three decisions confirmed with the user: **aggregates only** (no dimensional segment parsing), **latest annual (form 10-K) USD point** per concept, **filings→Event mapping included**.

**Files added**
- `Services/EdgarModels.cs` — deserialization records for companyfacts / submissions / company_tickers. Only `us-gaap` and `cik_str` need `[JsonPropertyName]` (System.Net.Http.Json uses web defaults: camelCase + case-insensitive).
- `Services/IStockApiClient.cs` + `StockApiClient.cs` — HTTP only. Base `https://data.sec.gov`; `User-Agent` set in ctor (SEC 403s without it); ticker map fetched from absolute `www.sec.gov` URL. SEC 404 → returns `null`; other transport failures throw (→ service maps to 503). Methods: `GetCompanyFacts`, `GetSubmissions`, `ResolveCik`.
- `Services/EdgarException.cs` — single exception carrying the HTTP status the controller should emit (503 / 422). One type, not a hierarchy.
- `Services/IStockService.cs` + `StockService.cs` — the only real logic. `RefreshAsync(company)`: fetch facts+filings (catch `HttpRequestException`/`TaskCanceledException`→503; null facts→422), clear prior `DataSource==EDGAR` rows, map Revenue (`Revenues`/`RevenueFromContract…`→`SEGMENT`, `Name="Revenue {fy}"`), COGS (`CostOfRevenue`/`CostOfGoodsAndServicesSold`), OPEX (`OperatingExpenses`), then dedupe-insert Events from `filings.recent` (10-K/10-Q→EARNINGS, 8-K→CORPORATE_ACTION; dedupe by `(Title, Date)` against DB + within batch). Returns refreshed `CompanyDto`.
- `Controllers/Api/StockController.cs` — `POST /api/stock/refresh/{companyId:long}` (404 missing, 409 null Cik, then delegates; catches `EdgarException`→`StatusCode(ex.StatusCode)`), `GET /api/stock/resolve/{ticker}`.
- Tests: `FakeStockApiClient.cs` (canned Apple data; null facts for any other CIK) + `StockTests.cs` (7 tests: happy path, events-with-unsupported-form-skipped, idempotency, 409 no-CIK, 422 not-a-filer, 404 missing, resolve 200/404).

**Files edited**
- `Repositories/{I,}RevenueSourceRepository.cs` + `{I,}CostSourceRepository.cs` — added `ClearByCompanyAndDataSource(companyId, source)` (soft-deletes matching active rows). Data access stays in repos per convention; no `AppDbContext` in the service.
- `Program.cs` — `AddHttpClient<IStockApiClient, StockApiClient>()` + `AddScoped<IStockService, StockService>()`.
- Tests `CustomWebApplicationFactory.cs` — `RemoveAll<IStockApiClient>()` then register `FakeStockApiClient` so the refresh flow runs offline/deterministic.

**Gotchas hit**
- `GetWithGraphRelations`'s `.Include(RevenueSources)` does **not** filter `DeletedAt` (no global query filter) → after a 2nd refresh the company-graph DTO still shows the soft-deleted (cleared) EDGAR rows next to the new ones. Idempotency is real at the data layer; verify it via the list endpoints (`/api/revenuesources`, `/api/costsources`) which go through `repo.GetAll()` (filters `DeletedAt==null`), **not** via the company graph. Did **not** change `GetWithGraphRelations` (shared with MVC graph + CompaniesController — out of scope).
- `StatusCodes` resolves with no explicit using (Web SDK implicit usings).

**Verification:** `dotnet test` → **111 passed, 0 failed** (was 103; +8 from this step). `database update` not run (MySQL down; no schema change anyway — only new code + repo methods).

**Stopped here.** Routing changed → background agent spawned to update `docs/sitemap.md` (per project CLAUDE.md rule). Next: STEP 3 (phase-1 UI, needs this live endpoint) or STEP 4 (reviewer, needs only 1a).

---

## STEP 3 — Phase-1 UI (split-screen entry + "Use as reference")

Left = source cells bound to a `RevenueSource`/`CostSource` row (pre-fill if EDGAR fetched it, else create on save). Right = the EDGAR response JSON. Selecting text + "Use as reference" writes one `SourceFieldReview` per cell (`Mark=null`), capturing `Endpoint`, `ReferencePointer`, `ReferenceSnapshot`, `ReferencedValue`. FK ordering: ensure the source row exists (insert if new) **before** the review row. See `extraction-pipeline.md` Phase 1.

This is MVC (Razor) per the existing UI stack — use the `create-new-page` skill. Biggest step; scope to one source type (cost or revenue) first.

### Acceptance
User can load a company, see EDGAR-filled cells + raw JSON, select proof, save → `SourceFieldReview` rows exist with the right FK + `Field` + frozen snapshot, `Mark` null.

### ✅ DONE — 2026-05-29 (completion report)
Step 3 complete and green. Scoped to **revenue only** per plan; three decisions confirmed with the user: **revenue source type first**, **right pane reuses `POST /api/stock/refresh/{companyId}`** (its `CompanyDto` JSON is the browsed response; the refresh already persists the EDGAR rows so left cells bind to real ids), **page at `/extraction` linked under the "More" nav**.

**Files added**
- `Models/ViewModels/ExtractionViewModels.cs` — `ExtractionIndexViewModel` (picker state), `ReferenceRequest` (left-cell state + the one cell being proved + selected proof), `ReferenceResult` record (`RevenueSourceId`, `ReviewId`, `Field`) returned to the page.
- `Controllers/ExtractionController.cs` — `[Route("extraction")]` MVC controller. `GET ""` → `Index(long? companyId)` (renders the split screen; `ViewBag.SourceTypes` for the classification select). `POST "reference"` → `Reference([FromBody] ReferenceRequest)`: (1) ensure the `RevenueSource` row exists — update if `RevenueSourceId` set, else `Add` a new `DataSource.MANUAL` row (FK ordering: row before review); (2) upsert one `SourceFieldReview` per `(RevenueSourceId, Field)` via `GetByCompany(...).FirstOrDefault(...)` (respects the unique index); re-referencing resets `Mark`/`Rationale`/`ReviewedAt`/`ReviewerModel` to null (stale-pass guard, gap #1). `Endpoint` stored = `"POST /api/stock/refresh/{companyId}"`. Returns `Json(ReferenceResult)`.
- `Views/Extraction/Index.cshtml` — split-screen. Company autocomplete (`_AutocompletePicker`, reuses `Companies/Lookup`) + "LOAD EDGAR" button. Left = revenue cells (Classification/Name/Value/%/Counterparty) each with a "Use as reference" button + ✓ flag, plus a "source row" `<select>` to pick which EDGAR row to bind. Right = `<pre id="response">` showing the refresh JSON. Vanilla JS: LOAD posts to the refresh endpoint, renders JSON, dedupes EDGAR revenue rows by name (newest id wins — handles the `GetWithGraphRelations` soft-delete echo from step 2) into the picker; "Use as reference" captures `window.getSelection()` text + char-offset pointer and POSTs to `/extraction/reference`, binding the returned row id so subsequent cells attach to the same row.
- `simple-bloomberg-terminal.Tests/ExtractionTests.cs` — 4 tests: VALUE reference writes unreviewed snapshot with right FK/Field/Relation + `Mark==null`; same-cell-twice upserts in place (one row, second snapshot wins, same `ReviewId`); null `RevenueSourceId` creates a `MANUAL` row then the review; missing snapshot → 400.

**Files edited**
- `Views/Shared/_Layout.cshtml` — "Extraction" link added to the "More" dropdown after "Cost Sources".

**Decisions / gotchas**
- **No antiforgery filter** on `POST /extraction/reference` — matches the existing unprotected API/graph JSON endpoints and keeps the fetch + tests simple; the view still emits `@Html.AntiForgeryToken()` and sends the `RequestVerificationToken` header, so adding `[ValidateAntiForgeryToken]` later is a one-liner.
- Plain `Controller` (not `[ApiController]`) → JSON body binding needs the explicit `[FromBody]`; validation is checked manually (no auto-400).
- Re-running refresh can echo soft-deleted EDGAR rows in the DTO (step-2 `GetWithGraphRelations` gotcha) → the row picker dedupes by name client-side; not a data-layer change.

**Verification:** `dotnet test` → **115 passed, 0 failed** (was 111; +4). Routing added → background agent spawned to update `docs/sitemap.md` (per project CLAUDE.md rule). No migration/schema change (`SourceFieldReview` table shipped in 1a).

### ➕ FOLLOW-UP — 2026-05-29 (right pane now shows REAL EDGAR, not the DB)
First-cut right pane reused `refresh` and so rendered the app's own `CompanyDto` (DB rows) — useless as *external* provenance (you'd be "proving" data against itself). Reworked, per user, to browse the **actual SEC responses** with proxy-into-pane. Two decisions confirmed: **browse both company facts + filings**, **proxy the filing text into the pane** (so proof can be selected).

**Live bug fixed first:** `StockApiClient` set the SEC `User-Agent` via `DefaultRequestHeaders.Add(...)` → `FormatException` at runtime (the email's `@` makes the string an invalid structured User-Agent). Never caught because tests use `FakeStockApiClient` (the real ctor never ran). Fix: `TryAddWithoutValidation`. Verified live: `GET /api/stock/resolve/AAPL` → real CIK.

**Files added/edited**
- `Services/EdgarModels.cs` — `EdgarRecent` extended with `ReportDate`/`AccessionNumber`/`PrimaryDocument`/`PrimaryDocDescription` as **optional positional params** (`= null`) so the existing `new EdgarRecent(Form:…, FilingDate:…)` call site stays valid.
- `Services/IStockApiClient.cs` + `StockApiClient.cs` — `GetCompanyFactsJson` (raw JSON string, not the parsed minimal type — pane must show *everything* SEC returns) and `GetFilingDocument(cik, accessionNoDashes, primaryDocument)` (absolute `www.sec.gov/Archives/...` URL).
- `Controllers/Api/StockController.cs` — 3 read-only proxy endpoints (no persistence): `GET facts/{companyId}` (passthrough companyfacts JSON), `GET filings/{companyId}` (filing list with a ready-built `documentUrl` per row), `GET filing/{companyId}?accession=&doc=` (one filing's primary document as `text/plain`). Shared `TryGetFilerCik` helper. Failure codes: 404 / 409 (no CIK) / 422 (not a filer / no filings) / 400 (filing missing accession+doc) / 503 (SEC unreachable).
- `Models/ViewModels/ExtractionViewModels.cs` — `ReferenceRequest.Endpoint` added; `ExtractionController.Reference` now stores the **actual** EDGAR response the proof came from (facts API path or a filing's `documentUrl`), falling back to the refresh URL.
- `Views/Extraction/Index.cshtml` — right pane reworked: "Company Facts" / "Filings" toolbar, a clickable filings `<ul>`, a `source: <endpoint>` label. LOAD still refreshes (pre-fills left) and now auto-opens Company Facts. Proof is selected from the real SEC text.
- Tests: `FakeStockApiClient` implements the 2 new methods + extended filing arrays; `StockTests` +5 (facts raw JSON, facts 422, filings list + documentUrl assembly, filing text, filing 400).

**Gotchas**
- Filing primary docs are 10-K `.htm` (inline XBRL) → proxied as raw HTML text: selectable but visually noisy. Server-side tag-stripping is the obvious later refinement (not done — out of scope).
- The original "reuse refresh" decision is now downgraded to *left-pane pre-fill only*; the refresh URL is just the `Endpoint` fallback when nothing else was browsed.

**Verification:** `dotnet test` → **120 passed, 0 failed** (was 115; +5). Live-checked against real SEC: `facts/1` returns Apple's full companyfacts, `filings/1` lists real recent filings (SD/144/4…) with correct `documentUrl`. Routing added → second background agent updated `docs/sitemap.md` with the 3 new routes.

**Stopped here.** Next: STEP 4 (phase-2 AI reviewer — needs only 1a, now also has real referenced rows + true external provenance to score).

---

## STEP 4 — Phase-2 AI reviewer

Service that pulls `GetUnreviewed()`, for each sends `{value from source row via FK, ReferenceSnapshot, Field}` to Claude, gets a 1/0 + rationale, writes `Mark`/`Rationale`/`ReviewedAt`/`ReviewerModel`. Reviewer prompt must permit arithmetic/inference (derived values — gap #2). Use the Anthropic SDK (see `claude-api` skill). Independent of step 3 — testable on hand-inserted rows right after 1a.

### Acceptance
Running the pass over seeded reviews sets `Mark` to 0/1 with a rationale; a snapshot that contradicts the value scores 0.

---

## STEP 5 — more external APIs
World Bank (macro → `GdpSnapshot`/`Country`), then Finnhub (non-US company fundamentals). Each = a refresh service cloning the EDGAR pattern + a new endpoint option in the phase-1 browser. See `external-api.md`.

## STEP 6 — relationship extraction (`RelatedCompanyId` edges)
EDGAR full-text search → fetch 10-K text → Claude extracts counterparty edges (Apple→TSMC) → write `CostSource`/`RevenueSource` rows with `RelatedCompanyId` set, `DataSource=CLAUDE_ESTIMATED`. The phase-2 reviewer (step 4) then verifies these. See `api-model.md`.

---

## Dependency summary
```
1a (data layer) ──┬──► 4 (reviewer)        [needs only 1a]
                  └──► 2 (EDGAR) ──► 3 (phase-1 UI)   [UI needs a live endpoint]
2 ──► 5 (more APIs)
2 + 4 ──► 6 (relationship extraction, verified by 4)
```
Minimum first PR = **1a**. It unblocks everything and ships independently.
