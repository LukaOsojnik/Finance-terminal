# Task 5: Integration Test Coverage Analysis

## 1. Test Infrastructure

### CustomWebApplicationFactory (`CustomWebApplicationFactory.cs`)

The test suite uses a `WebApplicationFactory<Program>` that:

- **Database**: Swaps the MySQL `AppDbContext` for an in-memory SQLite database. Each factory instance owns a single `SqliteConnection` kept open for the connection's lifetime, providing per-instance database isolation.
- **External dependencies**: Replaces `IStockApiClient` with `FakeStockApiClient` to avoid live SEC/EDGAR calls. No other external services are faked (no FMP, Yahoo, Perplexity, ExchangeRate, or DeepSeek stubs).
- **Seed data** (deterministic, identity values are predictable):
  - 2 countries: US (id=1), DE (id=2)
  - 2 companies: Apple (id=1, no sources), Microsoft (id=2, has "Cloud" RevenueSource)
  - 1 Event linked to US + Apple
  - 2 TradeBlocs: EU (id=1, no members), NAFTA (id=2, member US)
  - 1 CountryDetails on US (id=1)
  - 1 CountryAdvantage on US (id=1)
  - 1 CountryChallenge on US (id=1)
  - 1 GdpSnapshot on US, year 2023 (id=1)
  - 1 RevenueSource on Microsoft (id=1)
  - 0 CostSources seeded
- **Missing ID constant**: `999999` (never seeded)
- **Environment**: `"Testing"` — this matters for auth middleware behavior (see Section 4).

### ApiTestBase (`ApiTestBase.cs`)

- Abstract base class implementing `IDisposable`
- Creates a new `CustomWebApplicationFactory` per test class instance
- xUnit creates a new test class instance per test method, giving each test its own isolated database
- Exposes `Client` (`HttpClient`) and `Factory` for tests

### FakeStockApiClient (`FakeStockApiClient.cs`)

- Deterministic stand-in for `IStockApiClient`
- Canned EDGAR facts/filings only for Apple's CIK (`0000320193`):
  - Revenue: $383B, CostOfRevenue: $214B, OperatingExpenses: $55B (all FY 2023)
  - Submissions: 10-K, 8-K, Form 4 (the mapper ignores Form 4)
  - Resolve: only `"AAPL"` maps to `0000320193`
- Any other CIK returns `null` facts (service maps this to 422 "not an SEC filer")
- Filing document returns a canned text string

---

## 2. Per-Entity Test Coverage Table

| Entity | GET all | GET all (search) | GET by id (200) | GET by id (404) | POST (201) | POST (400) | PUT (200) | PUT (404) | DELETE (204) | DELETE (404) | DELETE (409) | Validation beyond Required |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| **Company** | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No |
| **Country** | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **CountryAdvantage** | Yes | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **CountryChallenge** | Yes | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **CountryDetails** | Yes | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **CostSource** | Yes (empty) | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **Event** | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **GdpSnapshot** | Yes | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | Year [1800,2200] |
| **RevenueSource** | Yes | No | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No | No |
| **TradeBloc** | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | Yes | No |

### Non-CRUD API endpoints tested

| Endpoint | Controller | Tests |
|---|---|---|
| `POST /api/stock/refresh/{id}` | StockController | Happy path (sources persisted), idempotency (no duplicates), no CIK -> 409, unknown CIK -> 422, missing -> 404 |
| `GET /api/stock/resolve/{ticker}` | StockController | Known ticker -> 200 + CIK, unknown -> 404 |
| `GET /api/stock/facts/{id}` | StockController | Valid -> 200 + raw JSON, unknown -> 422 |
| `GET /api/stock/filings/{id}` | StockController | Valid -> 200 + filing list with document URL |
| `GET /api/stock/filing/{id}` | StockController | Valid -> 200 + text, missing query params -> 400 |
| `GET /api/graph/company?cik=` | GraphController | Existing -> 200 + graph, hub-and-leaf projection, unknown -> 404, non-padded CIK -> 404, blank -> 400 |
| `POST /extraction/reference` | ExtractionController | Creates review, upserts same cell twice, creates row+review, missing snapshot -> 400, filing soft-delete revival |
| `GET /extraction/references/{id}` | ExtractionController | Returns saved pointers |

### Repository-level tests (non-API)

| Test class | What it tests |
|---|---|
| **SourceCascadeTests** | `FilingRepository.SoftDeleteSourceCluster` — cascade deletes shared filings, sibling sources. Three scenarios: shared filing cluster, multiple filings per source, no filing. |

---

## 3. Detailed Analysis Per Test File

### CompanyTests (11 tests) — CompaniesController

Fullest coverage of any entity. Tests:
- **GET all**: seeded companies returned; query with `?q=App` filters via `LIKE`
- **GET by id**: validates nested `Country` DTO is populated; validates nested `RevenueSources` list; 404 for missing
- **POST**: 201 with `Location` header and follow-up GET proves persistence; 400 for missing required fields
- **PUT**: changes are persisted and verifiable via follow-up GET; 404 for missing ID
- **DELETE**: soft-delete returns 204, follow-up GET returns 404; 409 when active `RevenueSource` blocks deletion; 404 for missing

**What's missing**: No PUT validation error test (400 on bad data), no "duplicate CIK" or other conflict tests, no POST with invalid FK (non-existent countryId).

### CountryTests (9 tests) — CountriesController

Covers all CRUD operations. Tests:
- **GET all**: seeded; search with `?q=United`
- **GET by id**: valid + 404
- **POST**: 201 with `Location`; 400 for missing required
- **PUT**: 200 with changed fields; 404 for missing
- **DELETE**: creates a standalone country first (no FK references), then deletes; 404 for missing

**What's missing**: No 409 conflict (e.g., delete US which has companies referencing it). The test creates a fresh country to avoid this. No PUT validation error test. No duplicate code test. No `GET all` search without query (already covered).

### CountryAdvantageTests (8 tests) — CountryAdvantagesController

Standard uniform CRUD. Tests:
- **GET all**, **GET by id** (valid + 404)
- **POST** (201 + 400), **PUT** (200 + 404), **DELETE** (204 + 404)

**What's missing**: No `GET all` with search/query. No PUT validation. No 409 conflict (references from other entities would likely not block this entity type).

### CountryChallengeTests (8 tests) — CountryChallengesController

Identical pattern to CountryAdvantageTests. Same gaps.

### CountryDetailsTests (8 tests) — CountryDetailsController

Notable because this entity is keyed 1:1 by `CountryId` (no identity column). Tests:
- Uses DE (id=2) as the "no details yet" country for POST
- All standard CRUD operations covered

**What's missing**: No search/query test. The 404 for GET by id uses `MissingId` (999999), but a valid country without details (Germany, id=2) would also 404 before POST. No test for creating duplicate details for the same country (should 409 or replace).

### CostSourceTests (8 tests) — CostSourcesController

No CostSources seeded. Tests adapt by creating rows via the API first. Tests:
- **GET all** returns empty list (200)
- **GET by id**: uses `CreateCostSourceAsync` helper to create a row, then fetches it
- All remaining CRUD operations standard

**What's missing**: No search/query test. No test for creating a CostSource with a non-existent `companyId`.

### EventTests (8 tests) — EventsController

M:N relations tested. Notable:
- **GET by id**: validates nested `Countries` and `Companies` lists in response
- **POST**: sends `countryIds`/`companyIds` arrays, re-fetches via `Location` to prove persistence
- **PUT**: replaces membership (drops Apple, keeps US), verifies `Companies` is empty
- **DELETE**: standard 204 + 404

**What's missing**: No 409 conflict. No test with empty or null arrays for M:N relations. No test for `with query` filter that returns zero results. No test for POST with invalid countryId/companyId.

### GdpSnapshotTests (9 tests) — GdpSnapshotsController

Only entity with a validation test beyond `[Required]`:
- **Create**: standard 201 + 400; **also** tests `Year` out of `[Range(1800, 2200)]` returns 400
- All other CRUD standard

**What's missing**: No search/query test. No PUT validation error. No test for duplicate (countryId, year) unique constraint.

### RevenueSourceTests (8 tests) — RevenueSourcesController

Standard uniform CRUD. Tests:
- Seeded "Cloud" RevenueSource on Microsoft
- All standard CRUD operations

**What's missing**: No search/query test. No test for creating with non-existent `companyId`. No PUT validation error.

### TradeBlocTests (10 tests) — TradeBlocsController

M:N with country members. Notable:
- **GET all**: seeded + search with `?q=European`
- **GET by id**: validates nested `Countries` list for NAFTA (member US)
- **POST**: sends `countryIds` array, re-fetches to prove members persisted
- **DELETE**: 204 for EU (no members), 409 for NAFTA (has member US), 404 for missing

**What's missing**: No PUT validation error. No test for duplicate code.

### StockTests (11 tests) — StockController

Specialized read/refresh endpoint, not CRUD. Tests:
- **Refresh**: happy path (sources persisted from EDGAR), does not create filing events, idempotency (two runs no duplicates), company without CIK -> 409, CIK not SEC filer -> 422, missing company -> 404
- **Resolve**: known ticker -> 200 + CIK, unknown -> 404
- **Facts**: valid CIK -> 200 + raw JSON, unknown CIK -> 422
- **Filings**: lists with correct `DocumentUrl` (SEC archive URL construction)
- **Filing text**: valid document -> 200, missing query params -> 400

**What's missing**: Auth tests (no `[Authorize]` on StockController endpoints). No test for EDGAR service failure / network error (the fake always succeeds or returns null).

### ExtractionTests (6 tests) — ExtractionController

Business process, not CRUD. Tests:
- **Reference**: creates `SourceFieldReview` with correct FKs, field, snapshot, and `Mark==null`
- **Same cell twice**: upserts in place (same `ReviewId` reused)
- **No source row**: creates revenue source then review
- **Get references**: returns saved pointer with correct values
- **Missing snapshot**: 400 validation
- **Filing soft-delete revival**: soft-deleted filing is revived (not duplicated) on re-reference

**What's missing**: The `[Authorize(Roles = "Admin,Manager")]` on ExtractionController appears not to block tests (see Section 4). Many extraction actions untested: `Save`, `SaveBatch`, `Review`, `AutoExtract`, `ScanAuto`, `ScanAutoAsync`, `ScanJobs`, `Chat`, `DiscoverRelated`, `LinkCounterparty`. Most of these require external AI/API calls (DeepSeek, Perplexity, FMP, Yahoo) and cannot be tested without fakes for those services.

### GraphApiTests (5 tests) — GraphController

Read-only non-CRUD. Tests:
- Existing CIK returns graph centered on company
- Hub-and-leaf projection (hub-revenue + leaf nodes)
- Unknown CIK -> 404
- Non-padded CIK -> 404 (literal match, no zero-padding normalization)
- Blank CIK -> 400

**What's missing**: No test for a company with no related data (no events, no sources). No test with invalid CIK format (e.g., too long, with dashes).

### SourceCascadeTests (3 tests) — FilingRepository

Repository-level (not API). Tests:
- **Shared filing cluster**: deleting one source cascades to all sources citing the same filing
- **Multiple filings per source**: deleting a source with two filings removes both
- **No filing**: deleting a source without a filing only removes that source and its reviews

**What's missing**: No test for the COST path (all test data uses REVENUE). No test for orphaned records or edge cases like circular references.

---

## 4. Authorization / Authentication Test Coverage

### Current state: No authorization tests exist

The test suite does **not** set up or test any authentication/authorization. Key observations:

1. **Most API controllers lack `[Authorize]`**: CompaniesController, CountriesController, CountryAdvantagesController, CountryChallengesController, CountryDetailsController, CostSourcesController, EventsController, GdpSnapshotsController, RevenueSourcesController, TradeBlocsController, StockController, GraphController — none have the `[Authorize]` attribute (based on the grep for `[ApiController]` which showed only that attribute).

2. **ExtractionController has `[Authorize(Roles = "Admin,Manager")]`** but its tests pass without any authentication setup. This suggests either:
   - The `"Testing"` environment disables authorization middleware
   - Or the test is inadvertently bypassing auth

3. **No auth-proxy or test authentication handler** is registered in `CustomWebApplicationFactory`.

**Recommendation**: If any controller gains `[Authorize]` in the future, tests will break without a test authentication handler. Consider adding an `AuthHandler` or using `WebApplicationFactory`'s `ConfigureServices` to stub auth for testing.

---

## 5. Gaps and Recommendations

### 5.1. Gaps within tested CRUD entities

| Gap | Severity | Affected entities |
|---|---|---|
| No PUT validation error (400) tests | Medium | All CRUD entities |
| No GET all search/query tests | Low | CountryAdvantage, CountryChallenge, CountryDetails, CostSource, GdpSnapshot, RevenueSource |
| No duplicate/unique constraint violation tests | Medium | Country (code), GdpSnapshot (countryId+year), TradeBloc (code) |
| No invalid FK reference tests | Low | CostSource (bad companyId), RevenueSource (bad companyId), Event (bad countryId/companyId) |
| No conflict (409) on parent delete | Medium | Country (has companies referencing it) |

### 5.2. Untested API endpoints

| Endpoint | Controller | Notes |
|---|---|---|
| `GET /api/ticker/feed` | TickerController | Read-only feed; not CRUD but it's a registered API route. Depends on ICompanyRepository/ICountryRepository/IEventRepository which are backed by the seeded DB. Could trivially be tested. |
| `POST /impact/solve` | ImpactController | Not an API controller (MVC, route `impact`); requires `EventImpactService` which has no fake. |

### 5.3. External service fakes

| Service | Faked? | Used by |
|---|---|---|
| IStockApiClient (EDGAR) | Yes (FakeStockApiClient) | StockController, ExtractionController |
| IStockApiClient (ticker→CIK) | Yes (FakeStockApiClient) | StockController resolve |
| IFmpApiClient | No | ExtractionController (LinkCounterparty) |
| IYahooFinanceClient | No | ExtractionController (LinkCounterparty) |
| IExchangeRateApiClient | No | ExtractionController (LinkCounterparty) |
| IExtractionChatService (DeepSeek) | No | ExtractionController (Review, Chat) |
| IFilingExtractionService (DeepSeek) | No | ExtractionController (AutoExtract, ScanAuto) |
| ICounterpartyDiscovery (Perplexity) | No | ExtractionController (DiscoverRelated) |
| EventImpactService | No | ImpactController |

The missing fakes prevent testing most ExtractionController actions beyond the reference flow. This is acceptable if those actions are exercised through manual QA, but automated coverage is zero.

### 5.4. Structural observations

1. **No test for `POST` with empty body** — tests use minimal but not empty JSON objects; the framework's model binding for empty body returns 415 (Unsupported Media Type) which could be worth asserting.

2. **No pagination tests** — none of the GET all endpoints use `$skip`/`$top` patterns (if they exist).

3. **No `PATCH` support** — all updates use `PUT` (full replacement); this is consistent.

4. **Test count summary**: ~112 tests across 14 test classes.

### 5.5. Priority recommendations

1. **Add PUT validation error tests (400)** — the easiest gap to fill and the most likely to regress. Every Create_MissingRequired test has a PUT counterpart waiting to be written.

2. **Add duplicate unique-constraint tests** — Country code and GdpSnapshot (countryId, year) are the most likely to surface real bugs.

3. **Add Country 409 on delete** — deleting a country that has referencing companies should 409 (mirroring the Company/TradeBloc pattern).

4. **Add a test auth handler to the factory** — before any `[Authorize]` attribute is added to CRUD controllers.

5. **Write a `TickerFeedTests` class** — the `GET /api/ticker/feed` endpoint is trivial to test given the seeded DB, and it already has the required repositories wired through DI.
