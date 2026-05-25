# API Convention

How to build the JSON API layer for this project. Read this before adding or
changing anything under `Controllers/Api/`, `Dtos/`, or `Services/`.

## 1. Goal

Implement complete REST API support (CRUD + DTOs) for all entities where
business rules allow, plus one external data integration (official stock data
from SEC EDGAR).

This is a **2-point assignment requirement**:
- CRUD for every entity (GET all + search, GET by id, POST, PUT, DELETE).
- API must not expose unnecessary internal entity fields → use DTOs.
- Related data shown through nested DTOs where it makes sense.

## 2. Core decision: the API is ADDITIVE

The API layer is an **addition**, not a replacement. Nothing in the current
MVC pipeline changes.

```
Browser (HTML forms)           JSON client / external caller
       │                                │
       ▼                                ▼
 MVC Controllers                  API Controllers   ◄── NEW
 returns View(vm)                 returns Ok(dto)
 uses ViewModels                  uses DTOs
       │                                │
       └────────────┬───────────────────┘
                     ▼
               Repositories          ◄── SHARED, untouched
                     │
                     ▼
                  EF Core
```

Rules:
- Do **not** modify existing MVC controllers, Razor views, or ViewModels.
- API controllers call the **same repositories** the MVC controllers call.
- `TickerController` (`/api/ticker`) is the existing precedent — generalize it.

## 3. When to add a service layer (and when NOT to)

Default: **no service layer**. Repos are already the injectable data-access
boundary. Adding a service that just forwards one call to one repo is an empty
pass-through — forbidden (violates project CLAUDE.md "no abstraction without a
demonstrated problem").

- **Plain CRUD + DTO** → controller maps entity↔DTO and calls the repo
  directly. No service.
- **External stock integration** → one `StockService`. This is the only place
  with real logic (fetch foreign JSON, map, decide persist, fallback when the
  API is down). It earns a service because it *combines sources* and *decides*.

## 4. Folder layout

```
Controllers/Api/        ← one API controller per entity, [ApiController]
Dtos/                   ← request + response DTOs (nested where related)
Services/               ← StockService + IStockApiClient (stock flow ONLY)
MappingProfile.cs       ← AutoMapper profile (project root or Dtos/)
```

## 5. API controller conventions

- Attribute: `[ApiController]` + `[Route("api/[controller]")]` (e.g. `/api/companies`).
- Inject the existing repository (and `IMapper`). No `AppDbContext` in controllers.
- One controller per entity. CRUD surface:

| Verb + route                 | Repo call                  | Returns                       |
|------------------------------|----------------------------|-------------------------------|
| `GET  /api/companies`        | `GetAll()` / `Search(q)`   | `Ok(List<XxxDto>)`            |
| `GET  /api/companies/{id}`   | `GetById(id)`              | `Ok(XxxDto)` or `NotFound()`  |
| `POST /api/companies`        | `Add(entity)`             | `CreatedAtAction(..., dto)`   |
| `PUT  /api/companies/{id}`   | `Update(entity)`          | `Ok(dto)` or `NotFound()`     |
| `DELETE /api/companies/{id}` | `SoftDelete(id)`          | `NoContent()`                 |

- Search: when the repo has a `Search`/`Lookup` method, `GET all` accepts a
  `?q=` query param and routes to it; otherwise plain `GetAll()`.
- `[ApiController]` gives automatic model validation → invalid request DTOs
  return `400` with no extra code.

## 6. DELETE = soft delete + business rules

- `DELETE` maps to the existing `SoftDelete(id)` on each repo (sets `DeletedAt`).
  Do **not** hard-delete.
- "Where business rules allow": some deletes throw by design — e.g.
  `CompanyRepository.SoftDelete` throws `InvalidOperationException` when active
  revenue/cost sources exist. Catch these in the controller and return
  `409 Conflict` with the message. Entities whose rules forbid deletion expose
  no DELETE endpoint.

## 7. DTO conventions

- **Response DTOs** omit internal fields: never expose `DeletedAt`, raw audit
  fields, or bare FK ids when a nested object is more useful.
- **Request DTOs** (create/update) accept only client-settable fields. Server
  sets `Id`, timestamps, `DeletedAt`.
- **Nested DTOs** for related data where it makes sense:
  `CompanyDto { ..., CountryDto? Country, List<RevenueSourceDto> RevenueSources }`.
  Keep nesting shallow — one level deep unless a screen needs more. Avoid
  cycles (a related-company DTO should not re-nest its own relations).
- Use `record` types (matches existing `TickerItem`, `GraphNode` style).

## 8. Mapping — AutoMapper

Chosen approach: **AutoMapper** (convention-based, less per-entity boilerplate).

```csharp
// Program.cs
builder.Services.AddAutoMapper(typeof(Program));

// MappingProfile.cs
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<Company, CompanyDto>();      // nested maps resolve automatically
        CreateMap<CompanyCreateDto, Company>();
        // ... one pair per entity/DTO
    }
}

// controller
var dto = _mapper.Map<CompanyDto>(company);
```

- One `CreateMap` per direction needed. Nested DTOs map automatically when their
  own maps are registered.
- For updates, map the request DTO onto the existing tracked entity:
  `_mapper.Map(updateDto, entity)` then `repo.Update(entity)`.

## 9. External stock data — StockService + SEC EDGAR

Source: **SEC EDGAR** (free, no key, official US filings). `Company.Cik` is the
key (10-digit zero-padded). Endpoints in memory `reference_sec_edgar_api.md`.

### What the data feeds — the graph child rows, NOT Company scalars

The valuable EDGAR output lands in the entities the graph renders
(`RevenueSource`, `CostSource`, `Event`), tagged `DataSource.EDGAR`. It does
**not** overwrite hand-entered `Company.RevenueTotal`/`GrossMargin`.

### Layers

- `IStockApiClient` / `StockApiClient` — HTTP only: base URL `https://data.sec.gov`,
  required SEC `User-Agent` header (identifying contact email — SEC blocks
  requests without it), JSON deserialization. Methods:
  - `GetSubmissions(cik10)` → filings list (`/submissions/CIK{cik10}.json`)
  - `GetCompanyFacts(cik10)` → XBRL facts (`/api/xbrl/companyfacts/CIK{cik10}.json`)
  - `ResolveCik(ticker)` → CIK via `https://www.sec.gov/files/company_tickers.json`
  - Registered with `AddHttpClient<IStockApiClient, StockApiClient>()`.
- `StockService` — logic: call the client, map foreign payload to entities,
  persist via repos, fall back when EDGAR is down. Returns `CompanyDto`.

### EDGAR → entity mapping

| EDGAR source                          | Entity        | Field mapping                                                            |
|---------------------------------------|---------------|--------------------------------------------------------------------------|
| `companyfacts` us-gaap `Revenues` / `RevenueFromContractWithCustomerExcludingAssessedTax` | `RevenueSource` | `SourceType=SEGMENT`, `Value`, `Name="Revenue {period}"`, `DataSource=EDGAR`, `RelatedCompanyId=null` |
| `companyfacts` revenue **segment** concepts | `RevenueSource` | `SourceType=SEGMENT`, one row per segment, `DataSource=EDGAR`          |
| `companyfacts` `CostOfRevenue` / `CostOfGoodsAndServicesSold` | `CostSource` | `CostBase=COGS`, `Value`, `DataSource=EDGAR`, `RelatedCompanyId=null`     |
| `companyfacts` `OperatingExpenses`    | `CostSource`  | `CostBase=OPEX`, `Value`, `DataSource=EDGAR`                             |
| `submissions` form `10-K` / `10-Q`    | `Event`       | `Type=EARNINGS`, `Title="{form} filed {date}"`, `Date=filingDate`        |
| `submissions` form `8-K`              | `Event`       | `Type=CORPORATE_ACTION`, `Title`, `Date=filingDate`                      |

Notes:
- EDGAR aggregates have no counterparty, so `RelatedCompanyId` is always null on
  EDGAR-sourced rows — they appear in the REVENUE/COST hubs, not RELATED COMPANIES.
- `Event` is many-to-many with `Company` — add to `event.Companies` / `company.Events`.
- `Event` has no `DataSource` field; see idempotency below for how filings dedupe.

### Refresh flow (persist, idempotent)

```
POST /api/stock/refresh/{companyId}
  company = _companies.GetById(companyId)        // 404 if missing
  if company.Cik is null  -> 409 Conflict        // non-filer, no SEC data
  cik10 = company.Cik.PadLeft(10, '0')
  facts    = client.GetCompanyFacts(cik10)
  filings  = client.GetSubmissions(cik10)
  // idempotency: clear prior EDGAR-tagged rows for this company, then reinsert
  soft-delete RevenueSources/CostSources where CompanyId==id && DataSource==EDGAR
  insert mapped RevenueSource / CostSource / Event rows
  return CompanyDto (with nested sources + events)
```

- **Idempotency:** refresh wipes only `DataSource==EDGAR` rows, never `MANUAL`
  ones. For events (no DataSource field), dedupe by `(CompanyId, Title, Date)` —
  skip a filing if a matching event already exists.
- **New repo methods needed** (data access stays in repos, per convention):
  `IRevenueSourceRepository` / `ICostSourceRepository` need a
  "get/clear by company + DataSource" method. Add them — do not query
  `AppDbContext` from the service.

### Failure handling

- SEC unreachable / timeout → `503 Service Unavailable`.
- CIK not found at SEC (404) → `422 Unprocessable Entity` ("not an SEC filer").
- Respect SEC rate limit (~10 req/s); set the `User-Agent` or requests are blocked.
- Non-filers in seed data (Aramco, Samsung, Nestlé, LVMH, Reliance, Tencent)
  have null `Cik` → caught by the 409 check above.

### Endpoints

- `POST /api/stock/refresh/{companyId}` — fetch + persist, returns `CompanyDto`.
- `GET  /api/stock/resolve/{ticker}` — ticker→CIK helper (optional, read-only).

## 10. DI registration (Program.cs)

Match the existing `AddScoped<IXxxRepository, XxxRepository>()` style:
- AutoMapper: `builder.Services.AddAutoMapper(typeof(Program));`
- Stock client: `builder.Services.AddHttpClient<IStockApiClient, StockApiClient>();`
- Stock service: `builder.Services.AddScoped<IStockService, StockService>();`
- No new DI registration needed for plain CRUD controllers (they reuse repos).

## 11. Entities to cover

All ten current repos (skip a verb only where business rules forbid it):
Country, Company, Event, CountryDetails, TradeBloc, CountryAdvantage,
CountryChallenge, GdpSnapshot, RevenueSource, CostSource.

## 12. Per-entity checklist

For each entity:
1. Response DTO + create/update request DTO(s) in `Dtos/` (nested where useful).
2. `CreateMap` entries in `MappingProfile`.
3. `Controllers/Api/XxxController.cs` with `[ApiController]`, the 5 CRUD actions,
   search on GET-all if the repo supports it.
4. Map `DELETE` to `SoftDelete`; return `409` on business-rule exceptions.
5. Reuse the existing repo — do not add a service unless logic beyond CRUD appears.

## 13. Do NOT

- Do not add a service that only forwards to a repo.
- Do not touch existing MVC controllers, views, or ViewModels.
- Do not inject `AppDbContext` into controllers (use repos; add a repo method if
  a query doesn't fit — see `GetWithGraphRelations` precedent).
- Do not return entities directly or expose `DeletedAt`/internal fields.
