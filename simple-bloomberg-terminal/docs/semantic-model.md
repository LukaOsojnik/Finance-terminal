# Semantic DB Model

> Soft-delete pattern: every entity has a nullable `DeletedAt` (`DateTime?`); rows with `DeletedAt != null` are hidden from list/detail queries — repositories filter `Where(e => e.DeletedAt == null)` on every read. Migration: `20260516183211_AddSoftDeleteToAllEntities`.

## Tables / Classes

| Table | Class | Role |
|---|---|---|
| Countries | Country | Core geographic/economic entity |
| Companies | Company | Corporation linked to a country |
| CompanyFinancials | CompanyFinancial | Dated per-fiscal-period financial history for a Company (time series) |
| CompanyVolumeHistories | CompanyVolumeHistory | Weekly trading-volume history per Company, backfilled from Yahoo Finance's chart endpoint (price-feed time series) |
| Events | Event | Market event (earnings, sanctions, etc.) |
| TradeBlocs | TradeBloc | Economic bloc (EU, NAFTA, etc.) |
| CountryDetails | CountryDetails | 1:1 extended profile for a Country |
| CountryAdvantages | CountryAdvantage | Advantage bullet point for a Country |
| CountryChallenges | CountryChallenge | Challenge bullet point for a Country |
| GdpSnapshots | GdpSnapshot | Annual GDP data point for a Country |
| RevenueSources | RevenueSource | Revenue line item for a Company |
| CostSources | CostSource | Cost line item for a Company |
| CompanyRisks | CompanyRisk | Disclosed risk for a Company (SEC Item 1A/7A); Name + Scope + Note, no money figures |
| Filings | Filing | A SEC EDGAR filing used as proof for a cost/revenue source; identity is the EDGAR accession number |
| AspNetUsers | AppUser | Application user (ASP.NET Core Identity), extended with profile-picture metadata |
| UserApiKeys | UserApiKey | 1:1 store of a user's bring-your-own third-party API keys (encrypted) |
| StockIndices | StockIndex | A named market index (e.g. NASDAQ-100) as a membership set over Companies; the per-member sector breakdown is derived from members' `Company.Sector`, while index-level `Sector`/`Region`/`TotalMarketCap` are catalog grouping/sorting facets |
| IndexConstituents | IndexConstituent | Payload-carrying N:M junction between StockIndex and Company; carries the per-member `WeightPct` |
| IndexImportJobs | IndexImportJob | Persisted, globally-visible index-import job; stores the original import request verbatim so any user can *continue* a `Partial` run under their own FMP key. No FK relationships |
| FmpIndustryMappings | FmpIndustryMapping | Learned cache mapping each distinct (normalized) FMP vendor industry label → a GICS sub-industry; populated once by the LLM and reused so the same label never costs a second model call |

> **Identity layer:** `AppDbContext` derives from `IdentityDbContext<AppUser>` (was plain `DbContext`). This adds the standard ASP.NET Core Identity tables: **AspNetUsers** (mapped to `AppUser`), **AspNetRoles**, **AspNetUserRoles**, **AspNetUserClaims**, **AspNetRoleClaims**, **AspNetUserLogins**, **AspNetUserTokens**. Roles **Admin**, **Manager**, **User** are seeded at startup.

---

## Models & Key Properties

### Country
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Code | string | ISO code (US, DE, CN…) |
| Name | string | |
| Region | string | |
| CurrencyCode | string | |
| GdpUsd | double? | |
| Population | long? | |
| RiskRating | double? | |
| Notes | string? | |
| DeletedAt | DateTime? | Soft-delete timestamp |

### Company
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Name | string | |
| CountryId | long | FK → Countries |
| Sector | Sector? | GICS sector enum; **nullable** — NULL = unclassified. (A non-nullable `Sector` silently defaulted to ordinal 0 / ENERGY, making any company born without a sector look like an energy company; NULL can't masquerade as a real sector. The Sector ordinals are frozen — IoCore maps them to BEA matrix rows — so "unclassified" can't be a new enum member.) |
| ClassifyStatus | ClassifyStatus | Where this row sits in the GICS sub-industry classification pipeline (Pending → Resolved \| NoFit); default Pending. Drives the "Unclassified" report and the AI re-resolve flow |
| ClassificationLocked | bool | When true a human has pinned the GICS classification; the auto-backfill and the AI re-resolve must never overwrite it. Set when someone corrects the sub-industry by hand, so a vendor-label mis-fit (the cache can't disambiguate two companies sharing one label) stays corrected. Default false. Migration: `20260625141310_AddClassificationLocked` |
| FmpIndustry | string? | Raw vendor (FMP/sonar) industry label, stored verbatim (nullable `longtext`) — the finest, source-of-truth tier; kept so industry can be re-resolved without re-fetching FMP and as the LLM's strongest signal |
| GicsSubIndustry | GicsSubIndustry? | GICS 163-tier sub-industry, LLM-reasoned from `FmpIndustry`; stored as nullable int |
| Industry | GicsIndustry? | GICS 74-tier industry enum; now a **denormalized rollup** of `GicsSubIndustry` (via `GicsSubIndustry.GetIndustry()`), cached for cheap querying |
| RevenueTotal | double? | |
| GrossMargin | double? | |
| MarketCap | double? | USD market capitalization, sourced from FMP profile |
| AsOf | DateOnly? | |
| Cik | string? | SEC identifier |
| Type | CompanyType | PUBLIC (ticker-backed, real FMP/Yahoo data) / PRIVATE (no ticker, profile + estimated financials from web search); default PUBLIC |
| DeletedAt | DateTime? | Soft-delete timestamp |

Nav: `Financials` (ICollection<CompanyFinancial>) — the dated per-period history; Company itself keeps only the latest denormalized snapshot. `VolumeHistory` (ICollection<CompanyVolumeHistory>) — the weekly trading-volume time series.

### CompanyFinancial
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (cascade delete) |
| FiscalYear | int | |
| Period | FiscalPeriod | FY, Q1, Q2, Q3, Q4 |
| EndDate | DateOnly? | |
| ReportedCurrency | string? | |
| Source | DataSource | FMP (US filers, full data) / YAHOO (non-US fallback) / CLAUDE_ESTIMATED (private companies' AI-estimated financials) |
| CapturedAt | DateTime | |
| Revenue | double? | |
| CostOfRevenue | double? | |
| GrossProfit | double? | |
| OperatingIncome | double? | |
| Ebitda | double? | |
| NetIncome | double? | |
| Eps | double? | |
| GrossMargin | double? | |
| OperatingMargin | double? | |
| NetMargin | double? | |
| CurrentRatio | double? | |
| DebtToEquity | double? | |
| TotalCash | double? | |
| TotalDebt | double? | |
| OperatingCashFlow | double? | |
| FreeCashFlow | double? | |
| DeletedAt | DateTime? | Soft-delete timestamp |

Dated per-fiscal-period financial time series for a Company (Company holds only the latest denormalized snapshot). Unique index on `(CompanyId, FiscalYear, Period)`. Source = FMP for US filers (full data); YAHOO for non-US fallback (only Revenue + NetIncome, annual).

### CompanyVolumeHistory
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (cascade delete) |
| WeekStart | DateOnly | Monday of the week the volume bar covers |
| Volume | long | Shares traded that week (`long`; large-cap weeks exceed `int.MaxValue`) |
| CapturedAt | DateTime | |

Weekly trading-volume time series per Company, backfilled from Yahoo Finance's chart endpoint (`/v8/finance/chart?interval=1wk&range=max`) — the price-feed series powering the multi-year volume graph (distinct from CompanyFinancial's fiscal-period fundamentals). Unique index on `(CompanyId, WeekStart)` — the upsert key; re-fetching refreshes rows in place instead of duplicating. No soft-delete `DeletedAt` (the importer clears-and-reinserts). Migration: `20260624140324_AddCompanyVolumeHistory`.

### Event
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Title | string | |
| Type | EventType | EARNINGS, SANCTIONS, TRADE_DEAL… |
| Date | DateTime | |
| EndDate | DateTime? | |
| ImpactScore | double? | |
| Description | string? | |
| DeletedAt | DateTime? | Soft-delete timestamp |

### TradeBloc
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Name | string | |
| Code | string | |
| Description | string? | |
| FoundedDate | DateOnly? | |
| DeletedAt | DateTime? | Soft-delete timestamp |

### CountryDetails
| Property | Type | Notes |
|---|---|---|
| CountryId | long | PK + FK → Countries (shared PK) |
| MarketPosition | string | Summary paragraph |
| DeletedAt | DateTime? | Soft-delete timestamp |

### CountryAdvantage / CountryChallenge
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CountryId | long | FK → Countries |
| Text | string | Single bullet point |
| DeletedAt | DateTime? | Soft-delete timestamp |

### GdpSnapshot
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CountryId | long | FK → Countries |
| Year | int | |
| GdpUsd | double | |
| DeletedAt | DateTime? | Soft-delete timestamp |

### RevenueSource
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (owner) |
| RelatedCompanyId | long? | FK → Companies (counterparty) |
| SourceType | SourceType | CUSTOMER, SEGMENT, REGION, PRODUCT |
| Name | string | |
| Value | double? | |
| Percentage | double? | |
| Reference | string? | WHERE in the document the row came from: the SEC Item / note / subheading (e.g. "Item 7. Management's Discussion"), set by the extraction agent. Nullable `longtext` (migration `20260616113409_AddRevenueAndRiskReference`) |
| Evidence | string? | The exact verbatim substring from the filing backing the row — findable by a literal search in the document. One quote per row. Nullable `longtext` (migration `20260814074704_CollapseProofOntoSourceRows`) |
| FilingId | long? | FK → Filings (Restrict, indexed); the filing Reference/Evidence were taken from (null when they came from Company Facts or a web source). Nav: `Filing`. Migration `20260814074704_CollapseProofOntoSourceRows` |
| DataSource | DataSource? | EDGAR, MANUAL, CLAUDE_ESTIMATED… |
| DeletedAt | DateTime? | Soft-delete timestamp |
| Status | ContributionStatus | Review state; defaults to Approved (live). User contributions are Pending until a Manager rules on them |
| ContributedByUserId | string? | FK → AspNetUsers.Id (OnDelete SetNull, indexed); the user who proposed a pending row; null = system/admin write. Nav: `ContributedBy` |
| SupersedesId | long? | Self-reference (audit pointer, not a configured FK) to the live Approved row this pending edit would replace; null = brand-new addition |

### CostSource
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (owner) |
| RelatedCompanyId | long? | FK → Companies (counterparty) |
| CostBase | CostBase | COGS, OPEX, TOTAL_COSTS |
| Name | string | |
| Value | double? | |
| Percentage | double? | |
| Reference | string? | WHERE in the document the row came from: the SEC Item / note / subheading, set by the extraction agent. Nullable `longtext` (migration `20260616080209_AddCostSourceReference`) |
| Evidence | string? | The exact verbatim substring from the filing backing the row — findable by a literal search in the document. One quote per row. Nullable `longtext` (migration `20260814074704_CollapseProofOntoSourceRows`) |
| FilingId | long? | FK → Filings (Restrict, indexed); the filing Reference/Evidence were taken from (null when they came from Company Facts or a web source). Nav: `Filing`. Migration `20260814074704_CollapseProofOntoSourceRows` |
| DataSource | DataSource? | |
| DeletedAt | DateTime? | Soft-delete timestamp |
| Status | ContributionStatus | Review state; defaults to Approved (live). User contributions are Pending until a Manager rules on them |
| ContributedByUserId | string? | FK → AspNetUsers.Id (OnDelete SetNull, indexed); the user who proposed a pending row; null = system/admin write. Nav: `ContributedBy` |
| SupersedesId | long? | Self-reference (audit pointer, not a configured FK) to the live Approved row this pending edit would replace; null = brand-new addition |

### CompanyRisk
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (owner) |
| Scope | RiskScope | MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL |
| Name | string | |
| Note | string? | Free-text detail |
| Reference | string? | WHERE in the document the row came from: the SEC Item / note / subheading (e.g. "Item 1A. Risk Factors"), set by the extraction agent. Nullable `longtext` (migration `20260616113409_AddRevenueAndRiskReference`) |
| Evidence | string? | The exact verbatim substring from the filing backing the row — findable by a literal search in the document. One quote per row. Nullable `longtext` (migration `20260814074704_CollapseProofOntoSourceRows`) |
| FilingId | long? | FK → Filings (Restrict, indexed); the filing Reference/Evidence were taken from (null when they came from Company Facts or a web source). Nav: `Filing`. Migration `20260814074704_CollapseProofOntoSourceRows` |
| DataSource | DataSource? | |
| DeletedAt | DateTime? | Soft-delete timestamp |
| Status | ContributionStatus | Review state; defaults to Approved (live). User contributions are Pending until a Manager rules on them |
| ContributedByUserId | string? | FK → AspNetUsers.Id (OnDelete SetNull, indexed); the user who proposed a pending row; null = system/admin write. Nav: `ContributedBy` |
| SupersedesId | long? | Self-reference (audit pointer, not a configured FK) to the live Approved row this pending edit would replace; null = brand-new addition |

Disclosed risk extracted from SEC Item 1A risk factors / Item 7A market risk. Mirrors RevenueSource/CostSource but has no money/percentage/counterparty — just Name + Scope + Note. Its proof is the Reference/Evidence pair on the row itself, taken from `Filing`.

**Proof model.** All three source types (RevenueSource, CostSource, CompanyRisk) carry their proof on the row: one `Reference` (where in the document), one `Evidence` (the verbatim substring) and one `FilingId` (the document). This replaced a per-field `SourceFieldReview` table — dropped in migration `20260814074704_CollapseProofOntoSourceRows`, which backfilled each row's Evidence/FilingId from its surviving review. Since one filing extraction already produces N source rows, per-cell proof rows were redundant: on the AI path all of a row's reviews carried a byte-identical snapshot, and the per-field grading columns (Mark/Rationale/ReviewedAt/ReviewerModel) were never written.

### Filing
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies |
| AccessionNumber | string | EDGAR accession number; **unique** |
| Form | string? | Filing form type (10-K, 8-K…) |
| FilingDate | DateTime? | |
| PrimaryDocUrl | string? | |
| DeletedAt | DateTime? | Soft-delete timestamp |

Constraints: unique index on `AccessionNumber` — EDGAR accession numbers are globally unique, so there is one Filing row per filing, shared by every source row that cites it (upsert-by-accession). The link is one filing per source row (`RevenueSource`/`CostSource`/`CompanyRisk.FilingId`). Migration: `20260603210530_AddFiling`; the link moved to the (now dropped) per-field review table in `20260603213908_MoveFilingLinkToReview` and back onto the source rows in `20260814074704_CollapseProofOntoSourceRows`.

### AppUser
Extends ASP.NET Core Identity's `IdentityUser` (maps to **AspNetUsers**). Standard Identity columns (Id, UserName, NormalizedUserName, Email, NormalizedEmail, PasswordHash, SecurityStamp, etc.) come from the base class; only the added columns are listed below.

| Property | Type | Notes |
|---|---|---|
| ProfilePictureData | byte[]? | Raw image bytes of the uploaded profile picture, stored in the DB row (mapped to `longblob` in MySQL); null = no picture. Served back by `AccountController.Picture` |
| OriginalFileName | string? | Original uploaded file name |
| ContentType | string? | MIME content type of the upload |
| SizeBytes | long? | File size in bytes |
| UploadedAt | DateTime? | Upload timestamp |

Stores a single profile-picture's bytes + metadata; the image bytes live in the DB row (as `longblob`), not on disk, so they survive container redeploys (ephemeral filesystems lose disk files). Note: AppUser does not use the soft-delete `DeletedAt` pattern (Identity-managed).

### UserApiKey
| Property | Type | Notes |
|---|---|---|
| UserId | string | PK + FK → AspNetUsers.Id (shared PK, 1:1) |
| DeepSeekKey | string? | Ciphertext (Data Protection API); null = not provided |
| FmpKey | string? | Ciphertext (Data Protection API); null = not provided |
| PerplexityKey | string? | Ciphertext (Data Protection API); null = not provided |
| KimiKey | string? | Ciphertext (Data Protection API); null = not provided |
| OpenAiKey | string? | Ciphertext (Data Protection API); null = not provided |
| AnthropicKey | string? | Ciphertext (Data Protection API); null = not provided |
| ParsingProvider | string? | **Plaintext** (not a secret). Which chat provider runs the parsing/structuring role — a `ChatProviderId` enum name (DeepSeek, Kimi, OpenAi, Anthropic); null = app default (DeepSeek) |
| ParsingModel | string? | **Plaintext**. Chosen model id for that provider (e.g. `deepseek-v4-pro`, `gpt-5`, `claude-sonnet-4-6`); null = provider default |
| WebSearchModel | string? | **Plaintext**. Chosen Perplexity sonar variant for the web-search role (e.g. `sonar-pro`); null = default `sonar-pro` |

A user's own (bring-your-own) third-party API keys for the keyed external services — one row per user, shared primary key with AppUser. Each *key* column holds the value as **ciphertext** encrypted by the ASP.NET Data Protection API (`UserApiKeyProvider`), never the raw key, so a DB dump leaks nothing usable. Null = the user hasn't provided that key. The `ParsingProvider` / `ParsingModel` / `WebSearchModel` columns are per-user model-routing settings, not secrets, and are stored **plaintext**; null = use the app default. Cascade-delete: keys vanish when the account is removed. No soft-delete `DeletedAt`. Migration: `20260614131546_AddUserApiKeys`.

### StockIndex
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Name | string | Display name, e.g. NASDAQ-100 |
| Code | string | Index key / Wikipedia-catalog key, e.g. `"nasdaq"`, `"sp500"`, `"dowjones"` |
| EtfProxy | string? | SPDR ETF ticker (e.g. `SPY`, `XLK`) when membership was imported from the State Street SPDR daily-holdings file; null for Wikipedia-sourced indices |
| Provider | string? | Where membership came from: `"Wikipedia"` (constituents scrape, cap-weighted from stored `Company.MarketCap`) or `"SPDR"` (State Street daily-holdings file, real published weights) |
| Description | string? | |
| Sector | Sector? | GICS sector used to GROUP indices on the catalog page. Null = a broad-market index (S&P 500, FTSE 100) spanning every sector; a value = a sector-specific index (XLK → INFORMATION_TECHNOLOGY). Inferred at import from the matched members' own `Company.Sector` (≥80% of weight in one sector → that sector, else null), with an optional Perplexity hint as fallback |
| Region | string? | Short display/grouping label, e.g. `"US"`, `"UK"`, `"Global"` |
| TotalMarketCap | double? | Snapshot of Σ(constituent `MarketCap`) stamped at import time — the "size" the catalog sorts by. Point-in-time like the cap-weights, so it lives next to `AsOf` rather than being recomputed live |
| AsOf | DateOnly? | When membership + weights were last imported |
| DeletedAt | DateTime? | Soft-delete timestamp |

Nav: `Constituents` (ICollection<IndexConstituent>). A named market index as a membership set over existing Company rows; it stores no per-member sector data — a sector/industry breakdown is derived at query time from each member's `Company.Sector`, weighted by the per-member `IndexConstituent.WeightPct`. The index-level `Sector` / `Region` / `TotalMarketCap` are catalog-page grouping/sorting facets stamped at import. Migration: `20260620185302_AddStockIndex`.

### IndexConstituent
| Property | Type | Notes |
|---|---|---|
| StockIndexId | long | Part of composite PK + FK → StockIndices (Cascade) |
| CompanyId | long | Part of composite PK + FK → Companies (Restrict) |
| WeightPct | double? | Index weight in percent (e.g. 8.91 = 8.91%), **cap-weighted** from `Company.MarketCap` at import (weight_i = cap_i / Σcap × 100); null = member has no stored MarketCap (excluded from the denominator) |

Composite PK `(StockIndexId, CompanyId)` doubles as the importer's upsert key (clears-and-reinserts on it). A payload-carrying junction (explicit association entity, not a pure skip navigation) realizing the N:M between StockIndex and Company. No `DeletedAt` — pure derived junction; the importer clears-and-reinserts rather than soft-deleting. Migration: `20260620185302_AddStockIndex`.

### IndexImportJob
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| Label | string | What's being imported, shown in the jobs list |
| Code | string | Stored import-request field, replayed verbatim on continue |
| Name | string | Stored import-request field, replayed verbatim on continue |
| WikiPage | string? | Stored import-request field, replayed verbatim on continue |
| EtfTicker | string? | Stored import-request field, replayed verbatim on continue |
| Sector | Sector? | Stored import-request field, replayed verbatim on continue |
| Region | string? | Stored import-request field, replayed verbatim on continue |
| Status | ImportJobStatus | Running / Done / Partial / Error; defaults to Running |
| IndexId | long? | Soft reference to the StockIndex this import produced/updated (**not** a configured FK); set once the import has produced an index |
| TotalConstituents | int | Last run's coverage — total constituents seen |
| Matched | int | Last run's coverage — members matched to existing Company rows |
| Provisioned | int | Last run's coverage — members auto-provisioned |
| Message | string? | Success/coverage summary |
| Error | string? | Failure detail |
| StartedBy | string? | Display name of the user who began the job (plain string, **not** a FK) |
| ContinuedBy | string? | Display name of the user who last continued the job (plain string, **not** a FK) |
| CreatedAt | DateTime | Defaults to `DateTime.UtcNow` |
| CompletedAt | DateTime? | When the run finished |

A persisted, globally-visible index-import job. Unlike the old in-memory job (which died with the process and was visible only to the starting browser), this row survives restarts and is shared across users: when one user's FMP key runs out mid-import the job lands in `Partial`, and another user can *continue* it under their own key — the re-run is idempotent (upserts the index by code, only provisions members not yet in the DB). The stored `Code`…`Region` fields hold the original request so a continue can replay it without re-supplying it. Live phase text is **not** stored here (it ticks too often); it lives in the in-memory `IndexImportJobStore` overlay while a run is active. **No FK relationships:** `StartedBy` / `ContinuedBy` are plain display strings (not FKs to AspNetUsers) and `IndexId` is a soft reference to StockIndex, not a configured FK. No soft-delete `DeletedAt`.

### FmpIndustryMapping
| Property | Type | Notes |
|---|---|---|
| Id | long | PK (identity) |
| Label | string | Normalized raw FMP vendor industry label (letters/digits, lowercased); max length 160; **unique** index `IX_FmpIndustryMappings_Label` — the upsert/lookup key |
| SubIndustry | GicsSubIndustry | GICS 163-tier sub-industry the label maps to; stored as int |

A learned vendor-label → GICS sub-industry cache. Each distinct FMP industry label is mapped once (by the LLM, on first sighting) and reused thereafter, so the same label never costs a second model call. `Label` is unique; the parent `GicsIndustry` / `Sector` are derived from `SubIndustry` (via `GicsSubIndustry.GetIndustry()` / `.GetSector()`), not stored. No FK relationships and no soft-delete `DeletedAt`. The unique index is configured in `OnModelCreating`. Migration: `20260624081845_AddGicsSubIndustryAndFmpLabelCache`.

---

## Relationships

```
Country ──────────────── CountryDetails     (1:1, shared PK)
Country ──────────────── Company            (1:N, FK CountryId)
Country ◄──────────────► TradeBloc          (N:M, junction table)
Country ◄──────────────► Event              (N:M, junction table)

CountryDetails ────────── CountryAdvantage  (1:N, FK CountryId — also reachable via Country directly)
CountryDetails ────────── CountryChallenge  (1:N, FK CountryId — also reachable via Country directly)
CountryDetails ────────── GdpSnapshot       (1:N, FK CountryId — also reachable via Country directly)

Note: Country.Advantages / Country.Challenges / Country.GdpHistory nav props exist as shortcuts.
      CountryDetails.Advantages / .Challenges / .GdpHistory are the same rows via a different path.

Company ──────────────── CompanyFinancial   (1:N, FK CompanyId, Cascade — nav: Financials)
Company ──────────────── CompanyVolumeHistory (1:N, FK CompanyId, Cascade — nav: VolumeHistory)
Company ──────────────── RevenueSource      (1:N, FK CompanyId — owner)
Company ──────────────── CostSource         (1:N, FK CompanyId — owner)
Company ──────────────── CompanyRisk        (1:N, FK CompanyId — owner)
Company ──────────────── RevenueSource      (1:N, FK RelatedCompanyId — counterparty, nav: RevenueFromDependents)
Company ──────────────── CostSource         (1:N, FK RelatedCompanyId — counterparty, nav: CostFromDependents)
Company ◄──────────────► Event              (N:M, junction table)

Company ──────────────── Filing             (1:N, FK CompanyId, Restrict)
Filing ───────────────── RevenueSource      (1:N, FK FilingId nullable, Restrict — the row's proof filing)
Filing ───────────────── CostSource         (1:N, FK FilingId nullable, Restrict — the row's proof filing)
Filing ───────────────── CompanyRisk        (1:N, FK FilingId nullable, Restrict — the row's proof filing)

Event   ◄──────────────► TradeBloc          (N:M, junction table)

AppUser ──────────────── UserApiKey         (1:1, shared PK UserId, Cascade)

StockIndex ───────────── IndexConstituent   (1:N, FK StockIndexId, Cascade — nav: Constituents)
Company ──────────────── IndexConstituent   (1:N, FK CompanyId, Restrict)
StockIndex ◄──N:M──────► Company             (via IndexConstituent payload-carrying junction; composite PK (StockIndexId, CompanyId))

AppUser ──────────────── RevenueSource      (1:N, FK ContributedByUserId nullable, SetNull — nav: ContributedBy; the contributing user)
AppUser ──────────────── CostSource         (1:N, FK ContributedByUserId nullable, SetNull — nav: ContributedBy; the contributing user)
AppUser ──────────────── CompanyRisk        (1:N, FK ContributedByUserId nullable, SetNull — nav: ContributedBy; the contributing user)
```

> **Contribution review:** RevenueSource / CostSource / CompanyRisk each gained `Status` (ContributionStatus, default Approved), `ContributedByUserId` (FK → AspNetUsers, SetNull, indexed; nav `ContributedBy`), and `SupersedesId` (self-reference audit pointer to the Approved row a pending edit replaces; not a configured FK). Migration: `20260614140315_AddContributionStatus`.

---

## Enums

| Enum | Values (abbreviated) |
|---|---|
| Sector | ENERGY, MATERIALS, FINANCIALS, INFORMATION_TECHNOLOGY, … (11 total) |
| CompanyType | PUBLIC, PRIVATE |
| ClassifyStatus | Pending (default, not yet classified), Resolved (a GICS sub-industry was assigned), NoFit (classifier ran constrained then unconstrained and still found no fitting sub-industry); tracks where a Company sits in the GICS sub-industry classification pipeline |
| GicsIndustry | SOFTWARE, AUTOMOBILES, SEMICONDUCTORS…, … (74 total); each value rolls up to one Sector |
| GicsSubIndustry | GICS 2023 sub-industry (163 total); each value rolls up to exactly one GicsIndustry via `GicsSubIndustryExtensions.GetIndustry()` (and one Sector via `.GetSector()`) |
| EventType | EARNINGS, CENTRAL_BANK, MACRO_DATA, TRADE_DEAL, SANCTIONS, … |
| SourceType | CUSTOMER, SEGMENT, REGION, PRODUCT |
| CostBase | COGS, OPEX, TOTAL_COSTS |
| DataSource | EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB, FMP, YAHOO |
| FiscalPeriod | FY, Q1, Q2, Q3, Q4 |
| RiskScope | MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL |
| ContributionStatus | Approved (=0, default/live), Pending, Rejected |
| ChatProviderId | DeepSeek, Kimi, OpenAi, Anthropic (parsing-role chat provider; stored as the enum *name* in UserApiKey.ParsingProvider) |
| ImportJobStatus | Running, Done, Partial (resumable — provisioning cut short, any user can continue under their own key), Error (failed before producing an index) |
