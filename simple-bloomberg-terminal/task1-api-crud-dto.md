# Task 1 Analysis: Complete API CRUD + DTO Support

## Analysis Date: 2026-06-12 (updated same day)
## Project: simple-bloomberg-terminal

---

## 1. Summary

The project has **17 entities** registered in `AppDbContext`, of which **16 have full CRUD API controllers** with complete DTO support. **1 entity (AppUser) has no API controller** — it is Identity-managed and intentionally out of scope.

All 16 implemented controllers follow a uniform, well-structured pattern:
- All use `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]`
- All have GET all (with `?q=` search), GET by id, POST, PUT, DELETE
- All use AutoMapper to map between entities and DTOs
- All use repository pattern for data access
- POST/PUT/DELETE require `Admin` or `Manager` roles; GET requires any authenticated user
- All use soft-delete via `DeletedAt` column (including `ScenarioShock`, which got a `DeletedAt` column added via migration `AddDeletedAtToScenarioShock`)

Two exceptions to the uniform pattern, both noted in their entity sections:
- **Filing.Create** goes through `repo.Upsert(...)` instead of a plain insert, because `AccessionNumber` is globally unique (including soft-deleted rows)
- **Scenario.GetAll/GetById/Search** use a filtered `Include(s => s.Shocks.Where(sh => sh.DeletedAt == null))` so soft-deleted shocks don't appear nested in `ScenarioDto`

The GraphController and StockController are special-purpose API controllers that do not provide CRUD operations.

---

## 2. Per-Entity CRUD Operations Status

### Legend
- **YES** = fully implemented
- **PARTIAL** = partially implemented
- **NO** = not implemented
- **N/A** = not applicable

| # | Entity | Controller | GET all (?q=) | GET by id | POST | PUT | DELETE | DTO Response | Request DTO | Nested DTOs |
|---|--------|-----------|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| 1 | **Country** | `CountriesController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 2 | **TradeBloc** | `TradeBlocsController.cs` | YES | YES | YES | YES | YES | YES | YES | YES (Countries) |
| 3 | **Company** | `CompaniesController.cs` | YES | YES | YES | YES | YES | YES | YES | YES (Country, RevenueSources, CostSources) |
| 4 | **Event** | `EventsController.cs` | YES | YES | YES | YES | YES | YES | YES | YES (Countries, Companies, TradeBlocs) |
| 5 | **CountryDetails** | `CountryDetailsController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 6 | **CountryAdvantage** | `CountryAdvantagesController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 7 | **CountryChallenge** | `CountryChallengesController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 8 | **GdpSnapshot** | `GdpSnapshotsController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 9 | **RevenueSource** | `RevenueSourcesController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 10 | **CostSource** | `CostSourcesController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 11 | **CompanyRisk** | `CompanyRisksController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 12 | **CompanyFinancial** | `CompanyFinancialsController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 13 | **SourceFieldReview** | `SourceFieldReviewsController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 14 | **Filing** | `FilingsController.cs` | YES | YES | YES (via `repo.Upsert`) | YES | YES | YES | YES | NO (flat) |
| 15 | **Scenario** | `ScenariosController.cs` | YES | YES | YES | YES | YES | YES | YES | YES (Shocks → ScenarioShockDto) |
| 16 | **ScenarioShock** | `ScenarioShocksController.cs` | YES | YES | YES | YES | YES | YES | YES | NO (flat) |
| 17 | **AppUser** | **MISSING** | NO | NO | NO | NO | NO | NO | NO | NO |

### Additional API Controllers (Non-CRUD)

| Controller | Endpoints | Purpose | DTOs? |
|-----------|-----------|---------|-------|
| `GraphController` | `GET api/graph/company?cik=` | Returns hub-and-spoke graph for a company by CIK | Uses `GraphResponse` (ViewModel) |
| `StockController` | `POST api/stock/refresh/{id}`, `GET api/stock/resolve/{ticker}`, `GET api/stock/facts/{id}`, `GET api/stock/filings/{id}`, `GET api/stock/filing/{id}` | EDGAR data refresh, CIK resolution, read-only SEC browser | Uses `CompanyDto` for refresh; anonymous types for others |

---

## 3. Per-Entity Detailed Analysis

### 3.1 Country (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CountriesController.cs`
- DTOs: `Dtos\CountryDtos.cs`
- Entity: `Models\Entities\Country.cs`

**Controller pattern:**
```csharp
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class CountriesController : ControllerBase
{
    // GET ?q= — search query parameter
    [HttpGet]
    public ActionResult<IEnumerable<CountryDto>> GetAll(string? q = null)

    // GET /{id:long}
    [HttpGet("{id:long}")]
    public ActionResult<CountryDto> GetById(long id)

    // POST — [Authorize(Roles = "Admin,Manager")]
    [HttpPost]
    public ActionResult<CountryDto> Create(CountryRequestDto dto)

    // PUT /{id:long} — [Authorize(Roles = "Admin,Manager")]
    [HttpPut("{id:long}")]
    public ActionResult<CountryDto> Update(long id, CountryRequestDto dto)

    // DELETE /{id:long} — [Authorize(Roles = "Admin,Manager")]
    [HttpDelete("{id:long}")]
    public IActionResult Delete(long id)
}
```

**DTOs:**
- `CountryDto` — flat record with scalars only (Id, Code, Name, Region, CurrencyCode, GdpUsd, Population, RiskRating, Notes)
- `CountryRequestDto` — class with `[Required]` on Code, Name, Region, CurrencyCode

**Nested DTOs:** None. Related collections (TradeBlocs, Events, Advantages, Challenges, GdpHistory, Details) are not exposed through the Country API. This is reasonable for a list-level response, but the single-entity GET could benefit from including related data.

**Mapping:**
```csharp
CreateMap<Country, CountryDto>();
CreateMap<CountryRequestDto, Country>();
```

**Gaps:**
- No nested DTOs for related entities (TradeBlocs, Events, Advantages, Challenges, GdpHistory do not appear in the response)

---

### 3.2 TradeBloc (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\TradeBlocsController.cs`
- DTOs: `Dtos\TradeBlocDtos.cs`
- Entity: `Models\Entities\TradeBloc.cs`

**Controller pattern:** Same as Country, with one difference:
- `Create` calls `_repo.Add(entity, dto.CountryIds)` — an overload that handles M:N membership
- `Update` calls `_repo.Update(entity, dto.CountryIds)` — same pattern

**DTOs:**
- `TradeBlocDto` — includes `List<RelatedRefDto> Countries` (nested)
- `TradeBlocRequestDto` — has `long[] CountryIds` for M:N relationship

**Nested DTOs:** YES — Countries via `RelatedRefDto` (Id + Name).

**Mapping:**
```csharp
CreateMap<TradeBloc, TradeBlocDto>();
CreateMap<TradeBlocRequestDto, TradeBloc>()
    .ForMember(t => t.Countries, o => o.Ignore()); // repo handles members
```

---

### 3.3 Company (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CompaniesController.cs`
- DTOs: `Dtos\CompanyDtos.cs`
- Entity: `Models\Entities\Company.cs`

**Controller pattern:** Same as standard, but:
- `GetById` uses `_repo.GetWithGraphRelations(id)` to eagerly load nested collections
- `Create` re-fetches the created entity via `GetWithGraphRelations` to return full nested data
- `Update` re-fetches after save via `GetWithGraphRelations`
- `Delete` wraps in try/catch for `InvalidOperationException` → `Conflict()`

**DTOs:**
- `CompanyDto` — includes `CountryDto? Country`, `List<RevenueSourceDto> RevenueSources`, `List<CostSourceDto> CostSources`
- `CompanyRequestDto` — has `long? CountryId` (with `[Required]`)

**Nested DTOs:** YES — Country, RevenueSources, CostSources are all fully expanded DTOs, not just RelatedRefDto.

**Observations:**
- `CompanyDto` does NOT include `CompanyRisks`, `Financials`, `Events`, `RevenueFromDependents`, or `CostFromDependents` navigation properties. Only `Country`, `RevenueSources`, and `CostSources` are surfaced.
- `CompanyRequestDto` does NOT include `MarketCap`, `CompanyType` (both exist on the entity but are settable).

---

### 3.4 Event (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\EventsController.cs`
- DTOs: `Dtos\EventDtos.cs`
- Entity: `Models\Entities\Event.cs`

**Controller pattern:** Same as TradeBloc with M:N relationship handling:
- `Create` calls `_repo.Add(entity, dto.CountryIds, dto.CompanyIds, dto.TradeBlocIds)`
- `Update` calls `_repo.Update(entity, dto.CountryIds, dto.CompanyIds, dto.TradeBlocIds)`

**DTOs:**
- `EventDto` — includes `List<RelatedRefDto> Countries`, `List<RelatedRefDto> Companies`, `List<RelatedRefDto> TradeBlocs`
- `EventRequestDto` — has `long[] CountryIds`, `long[] CompanyIds`, `long[] TradeBlocIds`

**Nested DTOs:** YES — Countries, Companies, TradeBlocs via `RelatedRefDto`.

**Mapping:**
```csharp
CreateMap<Event, EventDto>();
CreateMap<EventRequestDto, Event>()
    .ForMember(e => e.Countries, o => o.Ignore())
    .ForMember(e => e.Companies, o => o.Ignore())
    .ForMember(e => e.TradeBlocs, o => o.Ignore());
```

---

### 3.5 CountryDetails (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CountryDetailsController.cs`
- DTOs: `Dtos\CountryDetailsDtos.cs`
- Entity: `Models\Entities\CountryDetails.cs`

**Controller pattern:** Same as standard, but uses `countryId` route parameter instead of `id` because `CountryDetails` is keyed 1:1 by `CountryId` (no separate identity).

**DTOs:**
- `CountryDetailsDto` — flat (CountryId, MarketPosition)
- `CountryDetailsRequestDto` — flat (CountryId, MarketPosition)

**Nested DTOs:** None. Does not expose `Country` or `Advantages`/`Challenges`/`GdpHistory` navigation properties.

**Gaps:**
- No nested DTOs for related collections (Advantages, Challenges, GdpHistory) even though they are available as navigation properties on the entity.

---

### 3.6 CountryAdvantage (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CountryAdvantagesController.cs`
- DTOs: `Dtos\CountryAdvantageDtos.cs`
- Entity: `Models\Entities\CountryAdvantage.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `CountryAdvantageDto` — flat (Id, CountryId, Text)
- `CountryAdvantageRequestDto` — flat (CountryId, Text)

**Nested DTOs:** None.

---

### 3.7 CountryChallenge (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CountryChallengesController.cs`
- DTOs: `Dtos\CountryChallengeDtos.cs`
- Entity: `Models\Entities\CountryChallenge.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `CountryChallengeDto` — flat (Id, CountryId, Text)
- `CountryChallengeRequestDto` — flat (CountryId, Text)

**Nested DTOs:** None.

---

### 3.8 GdpSnapshot (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\GdpSnapshotsController.cs`
- DTOs: `Dtos\GdpSnapshotDtos.cs`
- Entity: `Models\Entities\GdpSnapshot.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `GdpSnapshotDto` — flat (Id, CountryId, Year, GdpUsd)
- `GdpSnapshotRequestDto` — flat (CountryId, Year, GdpUsd). `CountryId` has `[Required]`. Year has `[Range(1800, 2200)]`.

**Nested DTOs:** None.

---

### 3.9 RevenueSource (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\RevenueSourcesController.cs`
- DTOs: `Dtos\RevenueSourceDtos.cs`
- Entity: `Models\Entities\RevenueSource.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `RevenueSourceDto` — flat (Id, SourceType, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId)
- `RevenueSourceRequestDto` — flat (SourceType, Name, CompanyId, Value, Percentage, DataSource, RelatedCompanyId)

**Nested DTOs:** None. Does not expose `Company`, `RelatedCompany`, or `Reviews` navigation properties.

---

### 3.10 CostSource (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CostSourcesController.cs`
- DTOs: `Dtos\CostSourceDtos.cs`
- Entity: `Models\Entities\CostSource.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `CostSourceDto` — flat (Id, CostBase, Name, Value, Percentage, DataSource, CompanyId, RelatedCompanyId)
- `CostSourceRequestDto` — flat (CostBase, Name, CompanyId, Value, Percentage, DataSource, RelatedCompanyId)

**Nested DTOs:** None. Does not expose `Company`, `RelatedCompany`, or `Reviews`.

---

### 3.11 CompanyRisk (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CompanyRisksController.cs`
- DTOs: `Dtos\CompanyRiskDtos.cs`
- Entity: `Models\Entities\CompanyRisk.cs`

**Controller pattern:** Standard CRUD. Repo (`ICompanyRiskRepository`) already existed complete (GetAll, GetById, Search, Add, Update, SoftDelete) — only the controller/DTO/mapping layer was added.

**DTOs:**
- `CompanyRiskDto` — flat (Id, Scope, Name, Note, DataSource, CompanyId)
- `CompanyRiskRequestDto` — `[Required]` on Scope, Name, CompanyId

**Nested DTOs:** None. Does not expose `Company` or `Reviews`.

**Mapping:**
```csharp
CreateMap<CompanyRisk, CompanyRiskDto>();
CreateMap<CompanyRiskRequestDto, CompanyRisk>();
```

---

### 3.12 CompanyFinancial (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\CompanyFinancialsController.cs`
- Repo (new): `Repositories\ICompanyFinancialRepository.cs`, `Repositories\CompanyFinancialRepository.cs`
- DTOs: `Dtos\CompanyFinancialDtos.cs`
- Entity: `Models\Entities\CompanyFinancial.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `CompanyFinancialDto` — flat, all financial fields (Id, CompanyId, FiscalYear, Period, EndDate, ReportedCurrency, Source, CapturedAt, Revenue, CostOfRevenue, GrossProfit, OperatingIncome, Ebitda, NetIncome, Eps, GrossMargin, OperatingMargin, NetMargin, CurrentRatio, DebtToEquity, TotalCash, TotalDebt, OperatingCashFlow, FreeCashFlow)
- `CompanyFinancialRequestDto` — `[Required]` CompanyId, `[Range(1900,2200)]` FiscalYear, `[Required]` Period; all financial doubles optional. `CapturedAt` is **not** in the request DTO.

**Nested DTOs:** None. Does not expose `Company`.

**Observation:** `CompanyFinancialRepository.Add()` sets `entity.CapturedAt = DateTime.UtcNow` server-side before saving — clients cannot set this timestamp via the API.

**Mapping:**
```csharp
CreateMap<CompanyFinancial, CompanyFinancialDto>();
CreateMap<CompanyFinancialRequestDto, CompanyFinancial>();
```

---

### 3.13 SourceFieldReview (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\SourceFieldReviewsController.cs`
- Repo (extended): `Repositories\ISourceFieldReviewRepository.cs`, `Repositories\SourceFieldReviewRepository.cs` — added `GetAll()` and `Search(term)` (GetByCompany, GetUnreviewed, GetById, Add, Update, SoftDelete already existed)
- DTOs: `Dtos\SourceFieldReviewDtos.cs`
- Entity: `Models\Entities\SourceFieldReview.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `SourceFieldReviewDto` — flat, 16 fields (Id, CompanyId, Relation, RevenueSourceId?, CostSourceId?, CompanyRiskId?, Field, Endpoint, ReferencePointer, ReferenceSnapshot, ReferencedValue?, FilingId?, Mark?, Rationale?, ReviewedAt?, ReviewerModel?)
- `SourceFieldReviewRequestDto` — `[Required]` on CompanyId, Relation, Field, Endpoint, ReferencePointer, ReferenceSnapshot; the rest optional

**Nested DTOs:** None. Does not expose `Company`, `RevenueSource`, `CostSource`, `CompanyRisk`, or `Filing` navigation properties.

**Mapping:**
```csharp
CreateMap<SourceFieldReview, SourceFieldReviewDto>();
CreateMap<SourceFieldReviewRequestDto, SourceFieldReview>();
```

---

### 3.14 Filing (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\FilingsController.cs`
- Repo (extended): `Repositories\IFilingRepository.cs`, `Repositories\FilingRepository.cs` — added `GetAll()`, `Search(term)`, `SoftDelete(id)` (GetByAccession, GetById, GetByCompany, Add, Update, Upsert, SoftDeleteSourceCluster already existed)
- DTOs: `Dtos\FilingDtos.cs`
- Entity: `Models\Entities\Filing.cs`

**Controller pattern:** Standard CRUD **except `Create`**:
```csharp
var entity = _repo.Upsert(dto.CompanyId!.Value, dto.AccessionNumber, dto.Form, dto.FilingDate, dto.PrimaryDocUrl);
```
`AccessionNumber` has a unique index that spans soft-deleted rows too. A plain `Add()` after a soft-delete would hit a duplicate-key error, so Create finds-or-revives by accession instead of inserting blindly.

**DTOs:**
- `FilingDto` — flat (Id, CompanyId, AccessionNumber, Form, FilingDate, PrimaryDocUrl)
- `FilingRequestDto` — `[Required]` on CompanyId, AccessionNumber

**Nested DTOs:** None.

**Mapping:**
```csharp
// Create goes through repo.Upsert (accession-keyed), so only the Update direction is needed here.
CreateMap<Filing, FilingDto>();
CreateMap<FilingRequestDto, Filing>();
```

**Note:** This is a distinct table from `StockController`'s `/api/stock/filings/{id}` and `/api/stock/filing/{id}` endpoints, which are live EDGAR proxies and don't touch the `Filings` table.

---

### 3.15 Scenario (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\ScenariosController.cs`
- Repo (new): `Repositories\IScenarioRepository.cs`, `Repositories\ScenarioRepository.cs`
- DTOs: `Dtos\ScenarioDtos.cs`
- Entity: `Models\Entities\Scenario.cs`

**Controller pattern:** Standard CRUD.

**DTOs:**
- `ScenarioDto` — Id, Name, Description, `List<ScenarioShockDto> Shocks`
- `ScenarioRequestDto` — `[Required]` Name, optional Description

**Nested DTOs:** YES — `Shocks` via `ScenarioShockDto` (full expansion, not just `RelatedRefDto`).

**Repository detail:** `GetAll`/`GetById`/`Search` use a filtered include — `.Include(s => s.Shocks.Where(sh => sh.DeletedAt == null))` — so a soft-deleted shock doesn't appear nested inside `ScenarioDto.Shocks`.

**Mapping:**
```csharp
// nested Shocks -> ScenarioShockDto resolves via the ScenarioShock map below
CreateMap<Scenario, ScenarioDto>();
CreateMap<ScenarioRequestDto, Scenario>();
```

---

### 3.16 ScenarioShock (FULLY IMPLEMENTED)

**Files:**
- Controller: `Controllers\Api\ScenarioShocksController.cs`
- Repo (new): `Repositories\IScenarioShockRepository.cs`, `Repositories\ScenarioShockRepository.cs`
- DTOs: `Dtos\ScenarioShockDtos.cs`
- Entity: `Models\Entities\ScenarioShock.cs`

**Controller pattern:** Standard CRUD, exposed as its own top-level `/api/ScenarioShocks` resource (not nested under `/api/Scenarios`).

**DTOs:**
- `ScenarioShockDto` — flat (Id, ScenarioId, Kind, Target, Sector?, Factor?, Magnitude)
- `ScenarioShockRequestDto` — `[Required]` on ScenarioId, Kind, Target; Sector/Factor optional

**Nested DTOs:** None.

**Mapping:**
```csharp
CreateMap<ScenarioShock, ScenarioShockDto>();
CreateMap<ScenarioShockRequestDto, ScenarioShock>();
```

**Note:** `ScenarioShock` originally had no `DeletedAt` (it's an owned config row inside a `Scenario`, with no audit-trail need). Migration `20260612123143_AddDeletedAtToScenarioShock` added the column so `Delete` follows the same soft-delete pattern as every other entity, instead of a hard `Remove`.

---

### 3.17 AppUser (MISSING — out of scope)

- **File:** `Models\Entities\AppUser.cs`
- **Extends:** IdentityUser
- **Properties:** ProfilePicturePath, OriginalFileName, ContentType, SizeBytes, UploadedAt
- **Business context:** Application user with profile picture
- **Note:** Identity-managed (registration/profile via `Areas/Identity`); a CRUD API was deliberately not added — user management goes through ASP.NET Identity, not a generic entity API.

---

## 4. DTO Structure Analysis

### 4.1 DTO File Organization

All DTOs are in `Dtos/` as individual files grouped by entity. Pattern: one `.cs` file per entity group, each containing a response DTO (record) and a request DTO (class).

| File | Response DTO | Request DTO | Pattern |
|------|-------------|-------------|---------|
| `CountryDtos.cs` | `CountryDto` (record) | `CountryRequestDto` (class) | Flat |
| `TradeBlocDtos.cs` | `TradeBlocDto` (record) | `TradeBlocRequestDto` (class) | Nested RelatedRefDto |
| `CompanyDtos.cs` | `CompanyDto` (record) | `CompanyRequestDto` (class) | Nested full DTOs |
| `EventDtos.cs` | `EventDto` (record) | `EventRequestDto` (class) | Nested RelatedRefDto |
| `CountryDetailsDtos.cs` | `CountryDetailsDto` (record) | `CountryDetailsRequestDto` (class) | Flat |
| `CountryAdvantageDtos.cs` | `CountryAdvantageDto` (record) | `CountryAdvantageRequestDto` (class) | Flat |
| `CountryChallengeDtos.cs` | `CountryChallengeDto` (record) | `CountryChallengeRequestDto` (class) | Flat |
| `GdpSnapshotDtos.cs` | `GdpSnapshotDto` (record) | `GdpSnapshotRequestDto` (class) | Flat |
| `RevenueSourceDtos.cs` | `RevenueSourceDto` (record) | `RevenueSourceRequestDto` (class) | Flat |
| `CostSourceDtos.cs` | `CostSourceDto` (record) | `CostSourceRequestDto` (class) | Flat |
| `CompanyRiskDtos.cs` | `CompanyRiskDto` (record) | `CompanyRiskRequestDto` (class) | Flat |
| `CompanyFinancialDtos.cs` | `CompanyFinancialDto` (record) | `CompanyFinancialRequestDto` (class) | Flat |
| `FilingDtos.cs` | `FilingDto` (record) | `FilingRequestDto` (class) | Flat |
| `SourceFieldReviewDtos.cs` | `SourceFieldReviewDto` (record) | `SourceFieldReviewRequestDto` (class) | Flat |
| `ScenarioDtos.cs` | `ScenarioDto` (record) | `ScenarioRequestDto` (class) | Nested full DTOs (Shocks) |
| `ScenarioShockDtos.cs` | `ScenarioShockDto` (record) | `ScenarioShockRequestDto` (class) | Flat |
| `RelatedRefDto.cs` | `RelatedRefDto` (record) | — | Shared ref DTO |

### 4.2 Nested DTO Patterns

Three nesting levels are used:

**Level 1 — Flat (no nesting):**
- CountryDto, CountryDetailsDto, CountryAdvantageDto, CountryChallengeDto, GdpSnapshotDto, RevenueSourceDto, CostSourceDto, CompanyRiskDto, CompanyFinancialDto, FilingDto, SourceFieldReviewDto, ScenarioShockDto
- These expose only scalar properties and foreign key IDs

**Level 2 — Shallow refs via RelatedRefDto (Id + Name):**
- TradeBlocDto.Countries
- EventDto.Countries, EventDto.Companies, EventDto.TradeBlocs

**Level 3 — Full DTO expansion:**
- CompanyDto.Country → CountryDto (full expanded country)
- CompanyDto.RevenueSources → List<RevenueSourceDto>
- CompanyDto.CostSources → List<CostSourceDto>
- ScenarioDto.Shocks → List<ScenarioShockDto>

### 4.3 Shared DTOs

`RelatedRefDto` (`Dtos\RelatedRefDto.cs`) is a shared record used across multiple DTOs for nested references:
```csharp
public record RelatedRefDto(long Id, string Name);
```

Mapped for Country, Company, and TradeBloc in the MappingProfile.

### 4.4 Request DTO Design

All request DTOs are classes (not records) with `{ get; init; }` properties. `[Required]` annotations are used for mandatory fields. Nested M:N relationships are handled via `long[]` ID arrays (CountryIds, CompanyIds, TradeBlocIds).

---

## 5. Authorization Rules

| Role | Read (GET) | Write (POST/PUT/DELETE) |
|------|-----------|------------------------|
| **Any authenticated** | All GET endpoints | — |
| **Admin** | All GET endpoints | All write operations |
| **Manager** | All GET endpoints | All write operations |
| **Unauthenticated** | Denied (401) | Denied (401) |

Applied via `[Authorize]` at the class level and `[Authorize(Roles = "Admin,Manager")]` on mutation endpoints.

---

## 6. MappingProfile Coverage

The MappingProfile (`MappingProfile.cs`) covers all 16 entities that have API controllers:

| Map | Direction | Notes |
|-----|-----------|-------|
| Country <-> CountryDto | Both | — |
| Country -> RelatedRefDto | Entity -> Ref | For nested refs |
| Company <-> CompanyDto | Both | — |
| Company -> RelatedRefDto | Entity -> Ref | For nested refs |
| TradeBloc <-> TradeBlocDto | Both | Member ignore on Countries |
| TradeBloc -> RelatedRefDto | Entity -> Ref | For nested refs |
| CountryDetails <-> CountryDetailsDto | Both | — |
| CountryAdvantage <-> CountryAdvantageDto | Both | — |
| CountryChallenge <-> CountryChallengeDto | Both | — |
| GdpSnapshot <-> GdpSnapshotDto | Both | — |
| RevenueSource <-> RevenueSourceDto | Both | — |
| CostSource <-> CostSourceDto | Both | — |
| Event <-> EventDto | Both | Member ignore on nav collections |
| CompanyRisk <-> CompanyRiskDto | Both | — |
| CompanyFinancial <-> CompanyFinancialDto | Both | CapturedAt set server-side, not via request DTO |
| Filing <-> FilingDto | Both | Create uses `repo.Upsert`, not the RequestDto map |
| SourceFieldReview <-> SourceFieldReviewDto | Both | — |
| Scenario <-> ScenarioDto | Both | Nested Shocks resolve via the ScenarioShock map |
| ScenarioShock <-> ScenarioShockDto | Both | — |
| Company -> GraphResponse | Entity -> VM | Custom converter |

**Missing maps** (1 entity): AppUser — intentionally out of scope (Identity-managed).

---

## 7. Common Patterns Across All Implemented Controllers

All 16 CRUD controllers follow an identical structure:
1. Constructor injection of `IRepository` + `IMapper`
2. `[ApiController]`, `[Route("api/[controller]")]`, `[Authorize]` class attributes
3. `GetAll(string? q = null)` — delegates to repo `GetAll()` or `Search(q)`
4. `GetById(long id)` — returns `NotFound()` if null
5. `Create(RequestDto)` — maps, adds, returns `CreatedAtAction`
6. `Update(long id, RequestDto)` — fetches, maps onto entity, updates, returns Ok
7. `Delete(long id)` — checks existence, soft-deletes, returns `NoContent()`

Variations:
- **Company** uses `GetWithGraphRelations` for eager-loading nested data
- **TradeBloc** / **Event** pass ID arrays to repo for M:N relationship management
- **CountryDetails** uses `countryId` as the route parameter instead of `id` (1:1 key)
- **Company**, **TradeBloc** wrap `Delete` in try/catch for conflict handling
- **Filing** — `Create` calls `repo.Upsert(...)` instead of map+`Add()`, because `AccessionNumber` is globally unique across soft-deleted rows too
- **Scenario** — `GetAll`/`GetById`/`Search` use a filtered `Include(s => s.Shocks.Where(sh => sh.DeletedAt == null))` so the nested `ScenarioDto.Shocks` list excludes soft-deleted shocks
- **CompanyFinancial** — `repo.Add()` stamps `CapturedAt = DateTime.UtcNow` server-side; not part of the request DTO

---

## 8. Gaps and Recommendations

### 8.1 Missing API Controllers (1 entity)

| Entity | Priority | Rationale |
|--------|----------|-----------|
| **AppUser** | LOW | Identity-managed via `Areas/Identity`; a generic CRUD API was deliberately not added |

### 8.2 Missing Nested Data in Existing DTOs

Several controllers expose only flat DTOs even though their entities carry navigation properties that could be useful to API consumers:

| Controller | Entity Property | Current DTO | Recommendation |
|-----------|----------------|-------------|---------------|
| `CountriesController` | `Details` (CountryDetails) | Not exposed | Include as nested DTO on GET by id |
| `CountriesController` | `TradeBlocs`, `Advantages`, `Challenges`, `GdpHistory` | Not exposed | Consider for single-entity endpoint |
| `CountryDetailsController` | `Advantages`, `Challenges`, `GdpHistory` | Not exposed | Include as nested DTOs |
| `RevenueSourcesController` | `Reviews` | Not exposed | Include as nested DTO |
| `CostSourcesController` | `Reviews` | Not exposed | Include as nested DTO |
| `CompanyDto` | `CompanyRisks`, `Financials`, `Events` | Not exposed | Consider adding |

### 8.3 Missing Fields in CompanyRequestDto

`CompanyRequestDto` does not include:
- `MarketCap` (exists on entity)
- `CompanyType` (exists on entity, defaults to PUBLIC)
- These fields cannot be set via the API

### 8.4 Pagination

No controller implements pagination (`?page=&pageSize=`). The `?q=` search parameter exists, but all results are returned in a single response. For entities with potentially large datasets (GdpSnapshots, RevenueSources, CostSources), pagination would be beneficial.

### 8.5 Soft Delete vs Hard Delete

All controllers use `_repo.SoftDelete(id)`, which sets `DeletedAt` to the current timestamp. There is no hard-delete endpoint and no "restore" endpoint. This is consistent but should be documented for API consumers.

### 8.6 No PATCH support

There are no `[HttpPatch]` endpoints for partial updates. Clients must send the full resource representation for updates via PUT.

---

## 9. File Index

### Controllers/Api/
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CountriesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CompaniesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\EventsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\TradeBlocsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CountryDetailsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CountryAdvantagesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CountryChallengesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\GdpSnapshotsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\RevenueSourcesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CostSourcesController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CompanyRisksController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\CompanyFinancialsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\FilingsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\SourceFieldReviewsController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\ScenariosController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\ScenarioShocksController.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\GraphController.cs` (special, not CRUD)
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Controllers\Api\StockController.cs` (special, not CRUD)

### Dtos/
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\RelatedRefDto.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CountryDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\TradeBlocDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CompanyDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\EventDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CountryDetailsDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CountryAdvantageDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CountryChallengeDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\GdpSnapshotDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\RevenueSourceDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CostSourceDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CompanyRiskDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\CompanyFinancialDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\FilingDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\SourceFieldReviewDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\ScenarioDtos.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Dtos\ScenarioShockDtos.cs`

### Models/Entities/
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\Country.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\TradeBloc.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CountryDetails.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CountryAdvantage.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CountryChallenge.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\GdpSnapshot.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\Event.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\RevenueSource.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CostSource.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\Company.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\Filing.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\Scenario.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\ScenarioShock.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CompanyFinancial.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\CompanyRisk.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\SourceFieldReview.cs`
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\Models\Entities\AppUser.cs`

### Mapping
- `C:\Users\luka.osojnik\Documents\Coding\simple-bloomberg-terminal\simple-bloomberg-terminal\MappingProfile.cs`
