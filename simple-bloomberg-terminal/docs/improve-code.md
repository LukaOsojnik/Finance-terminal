# Code Maintainability Plan — Controllers & Services

Status: proposal. Generated from a full sweep of `Controllers/`, `Services/`, `Repositories/`.

Goal: cut duplication and decouple business logic from controllers so the codebase
is easier to change and test, **without** inventing abstractions that no real
duplication justifies. Each item below is anchored to concrete file:line evidence.

---

## Scope snapshot

| Area | Files | Lines | Worst offenders |
|---|---|---|---|
| Controllers | 37 (19 MVC, 18 API) | ~4,538 | `ExtractionController` (992), `CompaniesController` (651) |
| Services | 22 impl | ~3,000 | `CounterpartyDiscoveryService`, `FilingExtractionService` |
| Repositories | 14 impl | ~900 | 6 near-identical contribution repos |

Estimated removable/relocatable duplication: **~900–1000 lines**.

---

## Priority 1 — High value, low risk ✅ DONE (2026-06-15)

These are mechanical, well-isolated, and pay off immediately.

**Status:** all four items implemented; build clean (0 warnings), 179/179 tests pass.
- 1.1 — landed scoped to the **3** contribution repos (revenue/cost/risk); the 2 country repos
  were excluded as a different shape (no Status, Text-vs-Name) to avoid a leaky base. ~68 lines removed.
- 1.2 — `Services/LlmJson.cs` (`ParseObject` + salvage, `Str`, `Num`); FilingExtraction's
  number-tolerant `Str` and IndustryClassifier's direct parse left local on purpose. ~90 lines removed.
- 1.3 — `UserApiKeyProviderExtensions.RequireAsync`; applied to the 3 service `KeyAsync` copies.
  Controller call sites left as-is (their `keys` var double-duties as the detached-job snapshot).
- 1.4 — `EventsController.Today` now `DateTime.UtcNow.Date`.

### 1.1 ✅ Generic repository base for the 6 contribution repositories
**Problem.** `CompanyRiskRepository`, `CostSourceRepository`, `RevenueSourceRepository`,
`CountryAdvantageRepository`, `CountryChallengeRepository`, (+ partial `FilingRepository`)
repeat the same 8 methods word-for-word: `GetAll`, `GetAllPending`, `GetPendingByCompany`,
`GetById`, `Search`, `Add`, `Update`, `SoftDelete`, `ClearByCompanyAndDataSource`.
~48 redundant method bodies. The only per-repo differences are the `DbSet`, the
`Include`s, and the `OrderBy` keys.

Evidence: `CompanyRiskRepository.cs:10-72`, `CostSourceRepository.cs:10-76`,
`RevenueSourceRepository.cs:10-77` are 99% identical.

**Fix.** Introduce `ContributionRepositoryBase<T>` where `T` implements a shared
marker interface exposing `DeletedAt`, `Status`, `CompanyId`, `Name`. Base owns the
soft-delete + `Status == Approved` filter, `SoftDelete`, `Add`, `Update`, and a
`Search` template. Subclasses override only `Query()` (the `Include` chain) and the
ordering. Keep the existing interfaces (`ICompanyRiskRepository` etc.) so controllers
don't change.

Justification vs CLAUDE.md "no new abstractions": the duplication is demonstrated and
large; the base class removes ~250 lines and centralizes the soft-delete invariant
(currently copy-pasted, easy to get wrong).

**Risk.** Low. Behavior-preserving. Covered by existing repo tests.

### 1.2 ✅ Extract one JSON-envelope helper for LLM responses
**Problem.** The "slice from first `{` to last `}`, tolerate code fences, then
`JsonDocument.Parse`" trick is reimplemented in 6+ services, plus private `Str(...)`
and `Num(...)` helpers copied verbatim into 3 of them.

Evidence:
- Slice/parse: `CompanyProfileDiscoveryService.cs:98-146`, `CounterpartyDiscoveryService.cs:249-330`,
  `FilingExtractionService.cs:333-387`, `ReviewService.cs:104-119`, `IndustryClassifier.cs:43-52`.
- `Str`/`Num` duplicated: `CompanyProfileDiscoveryService.cs:127-146`,
  `CounterpartyDiscoveryService.cs:332-355`, `FilingExtractionService.cs:368-387`.

**Fix.** A static `LlmJson` helper: `TryExtractObject(string answer) -> JsonDocument?`
plus `Str(JsonElement, prop)` and `Num(JsonElement, prop)` (handling the `"null"`
literal, currency symbols, `%`). No new service, no DI — pure static, called where the
copies live today.

**Risk.** Low. Each call site swaps a private method for the shared one. Add unit tests
on the salvage/fence cases first (they encode hard-won model-output quirks).

### 1.3 ✅ Centralize the API-key fetch+validate (`KeyAsync`)
**Problem.** Identical `KeyAsync` (`get key -> throw MissingApiKeyException.X() if blank`)
in `DeepSeekClient.cs:29-35`, `CompanyProfileDiscoveryService.cs:30-36`,
`CounterpartyDiscoveryService.cs:53-59`. Controllers repeat the same check too
(`ExtractionController.cs:566,709,771,807`, `CompaniesController.cs:275`).

**Fix.** Add an extension on `IUserApiKeyProvider`:
`Task<string> RequireAsync(Func<UserApiKeys,string?> pick, Func<MissingApiKeyException> ifMissing, CancellationToken)`.
One line per call site. Controllers keep using `WriteMissingKeyAsync` to render the 424.

**Risk.** Low.

### 1.4 ✅ Replace the hardcoded test date in `EventsController`
**Problem.** `EventsController.cs:19` — `static readonly DateTime Today = new(2026, 4, 16)`.
A frozen date shipping in a controller is a latent bug.

**Fix.** Use `DateTime.UtcNow` (or inject a clock only if tests need determinism —
don't add `IClock` speculatively).

**Risk.** Low, but verify no view logic depends on the fixed value.

---

## Priority 2 — Decouple fat controllers

The two fat controllers mix HTTP concerns with real business logic. These are the
biggest maintainability wins but need care — extract behavior-preserving services.

### 2.1 `ExtractionController` (992 lines) → carve out extraction domain logic
**Problem.** Controller owns graph/contribution business rules, not just request handling.

Move to a service (e.g. extend existing `IFilingExtractionService`/`IReviewService` or a
new `IContributionWriter`):
- `UpsertRowByNode` + `UpsertRevenue`/`UpsertCost`/`UpsertRisk` — `ExtractionController.cs:302-428`
  (contribution status, supersession, reviewer gates: pure domain rules).
- `UpsertReviewByNode` — `:449-492` (review versioning, mark clearing).
- `EnsureReciprocal` — `:889-902` (bidirectional graph linking).
- `GetOrCreateCompanyAsync` — `:907-964` (FMP fetch + enrich + LLM classify; chains 3 services).

Leave streaming/NDJSON glue (`Chat :761`, `DiscoverRelated :798`) in the controller —
that's legitimately HTTP-shaped.

**Risk.** Medium. These methods touch multiple repositories and the contribution
state machine. Extract one method at a time; lean on existing extraction tests.

### 2.2 `CompaniesController` (651 lines) → CompanyDiscovery/Profile service
**Problem.** FMP integration, country resolution, ticker parsing, LLM classification all
inline.

Move:
- `BuildModelFromTickerAsync` + `Fetch` body — `CompaniesController.cs:111-176`.
- `BuildPrivateModelAsync` — `:372-404` (enum/date transformation).
- `Backfill` + `ResolveTickerForCik` — `:406-494` (bulk op + SEC CIK string parsing).
- `ResolveOrCreateCountry` — `:582-611` (REST Countries lookup + matching).
- `ResolveIndustryWithLlm` — `:572-576`.

Several already have services they should fully delegate to (`ITickerProfileEnricher`,
`ICompanyFinancialsService`, `IRestCountriesClient`). Goal: controller actions become
thin orchestration.

**Risk.** Medium. Background-task paths (`Rediscover :266-332`) capture keys via
`IUserApiKeyProvider.Set` — preserve that exactly when relocating.

### 2.3 `AccountController` — stop bypassing the repository layer + extract file I/O
**Problem.**
- Direct `DbContext` on `UserApiKeys` (`AccountController.cs:134,154,158,165`) — the only
  controller that bypasses repositories.
- Raw `File`/`Directory` calls (`:88-105,193-198`) and encrypt/decrypt logic (`:171-190`)
  inline.

**Fix.** Route API-key persistence through a repository/service (a `UserApiKeyRepository`
likely already implied by `IUserApiKeyProvider` — reuse it). Move file ops behind a small
`ProfilePictureService`. Move `AllowedTypes`/`MaxBytes` (`:25-32`) to config.

**Risk.** Low–medium.

### 2.4 `ContributionsController` — fix N+1 and move status transitions
**Problem.**
- `Email(userId)` calls `FindByIdAsync` synchronously inside a loop — N+1
  (`ContributionsController.cs:75-83`).
- `Approve`/`Reject` status-transition + soft-delete cascade is domain logic
  (`:135-180`).

**Fix.** Batch-load users once into a dict. Move the transition logic next to the
contribution repos / into the extraction service from 2.1.

**Risk.** Low–medium.

---

## Priority 3 — Consistency & smaller dedup

Lower payoff individually; do opportunistically.

### 3.1 HttpClient configuration is scattered
Six clients hardcode base URLs/timeouts and only 2 of 6 set a User-Agent.
Evidence: `StockApiClient.cs:20-24` (URL + email User-Agent), `YahooFinanceClient.cs:23`,
`ExchangeRateApiClient.cs:16`, `RestCountriesClient.cs:17`, `Sec2MdClient.cs:20-21`,
`DeepSeekClient.cs:26`.
**Fix.** Move base URLs/timeouts/User-Agent into `appsettings` + typed `HttpClient`
registration in `Program.cs` (`AddHttpClient<T>(c => ...)`). The email User-Agent in
`StockApiClient.cs:24` is hardcoded PII — config it. **Don't** build a custom client base
class; the framework's typed-client mechanism already covers this.

### 3.2 MVC CRUD Edit/Delete pattern repeated across 10+ controllers
`EditGet`/`EditPost` (`TryUpdateModelAsync` + repopulate dropdowns) and the
`Delete` try/catch-`InvalidOperationException` block are ~95% identical
(`RevenueSourcesController.cs:122-151` vs `CostSourcesController.cs:61-89`;
delete block in Countries/Events/TradeBlocs/CountryAdvantages/CountryChallenges/Gdp).
**Recommendation.** A generic MVC base controller is *possible* but risky — these views
differ in dropdown population and labels, and an inheritance base tends to leak. Prefer
small **shared helpers** (e.g. a `SoftDeleteRedirect(id)` extension and a `PopulateEnum`
helper) over a deep base class. Re-evaluate a base controller only if helpers don't
shrink it enough.

### 3.3 Generic API CRUD base controller (defer / decide explicitly)
20+ API controllers share an 80–90% identical `GetAll/GetById/Create/Update/Delete`
shape over `IRepository` + `IMapper`. A `CrudControllerBase<TEntity,TDto,TReq>` would
remove ~250 lines. **But** this is exactly the kind of abstraction CLAUDE.md warns
against if it obscures routing/auth. Several controllers have per-action auth nuances
(`[Authorize(Roles="Admin,Manager")]` on mutations). **Decision needed** before doing —
see open questions.

### 3.4 Enum→`SelectListItem` dropdown rendering
Repeated in `RevenueSourcesController.cs:163-166`, `EventsController.cs:125-132`,
`ExtractionController.cs:137-145`. One static helper
`EnumSelect<TEnum>()`. Trivial, low risk.

### 3.5 SEC CIK padding/trimming
`CompaniesController.cs:488-493`, `Api/StockController.cs:81-122` repeat
pad-left-10 / trim-leading-zero. Extract `Cik.Normalize(string)` / `Cik.Trim(string)`
static helpers.

### 3.6 Hardcoded model names / magic constants
Model fallbacks (`deepseek-v4-flash`, `deepseek-v4-pro`, `sonar-pro`) and tuning
constants (`MaxParallel`, `RecencyYears`, `ContextBudgetChars`, cache TTLs) are scattered
across services. Consolidate into a strongly-typed options class bound from config so
they're discoverable and tunable in one place. Evidence: `FilingExtractionService.cs:20,31,40`,
`ExtractionChatService.cs:30,34`, `ReviewService.cs:18`, `CounterpartyDiscoveryService.cs:50,61-63`.

---

## Explicitly NOT recommended

- **Custom HttpClient base class** — framework typed clients already solve it (3.1).
- **Deep MVC CRUD base controller** — views diverge too much; helpers are safer (3.2).
- **New mapping layer for MVC view models** — AutoMapper is already used in API; manual
  mapping in MVC controllers is fine where models are view-specific. Don't force uniformity.

---

## Suggested sequencing

1. P1 items (1.1–1.4) — isolated, high ROI, safe. Land first.
2. P2.3 / P2.4 (Account, Contributions) — bounded, clear wins.
3. P2.1 / P2.2 (the two fat controllers) — one extracted method per PR, behavior-preserving.
4. P3 — opportunistic, alongside related feature work.

---

## Open questions (need a decision before touching)

1. **API CRUD base controller (3.3):** worth the abstraction, or keep explicit per-controller
   for routing/auth clarity? This conflicts with the "no speculative abstractions" rule —
   needs an explicit call.
2. **Contribution logic home (2.1/2.4):** extend an existing service
   (`IReviewService`/`IFilingExtractionService`) or introduce one new `IContributionWriter`?
3. **Repository base (1.1):** confirm all 6 entities can share one marker interface
   (`DeletedAt`, `Status`, `CompanyId`, `Name`) — `CountryAdvantage`/`CountryChallenge`
   lack the `Pending`/`ClearByCompanyAndDataSource` methods, so they may need a thinner base.
