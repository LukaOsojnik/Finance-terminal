# Semantic DB Model

> Soft-delete pattern: every entity has a nullable `DeletedAt` (`DateTime?`); rows with `DeletedAt != null` are hidden from list/detail queries — repositories filter `Where(e => e.DeletedAt == null)` on every read. Migration: `20260516183211_AddSoftDeleteToAllEntities`.

## Tables / Classes

| Table | Class | Role |
|---|---|---|
| Countries | Country | Core geographic/economic entity |
| Companies | Company | Corporation linked to a country |
| CompanyFinancials | CompanyFinancial | Dated per-fiscal-period financial history for a Company (time series) |
| Events | Event | Market event (earnings, sanctions, etc.) |
| TradeBlocs | TradeBloc | Economic bloc (EU, NAFTA, etc.) |
| CountryDetails | CountryDetails | 1:1 extended profile for a Country |
| CountryAdvantages | CountryAdvantage | Advantage bullet point for a Country |
| CountryChallenges | CountryChallenge | Challenge bullet point for a Country |
| GdpSnapshots | GdpSnapshot | Annual GDP data point for a Country |
| RevenueSources | RevenueSource | Revenue line item for a Company |
| CostSources | CostSource | Cost line item for a Company |
| CompanyRisks | CompanyRisk | Disclosed risk for a Company (SEC Item 1A/7A); Name + Scope + Note, no money figures |
| SourceFieldReviews | SourceFieldReview | Per-field provenance (incl. the filing link) + AI verdict for one cell of a revenue/cost/risk source |
| Filings | Filing | A SEC EDGAR filing used as proof for a cost/revenue source; identity is the EDGAR accession number |
| AspNetUsers | AppUser | Application user (ASP.NET Core Identity), extended with profile-picture metadata |
| UserApiKeys | UserApiKey | 1:1 store of a user's bring-your-own third-party API keys (encrypted) |

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
| Sector | Sector | GICS sector enum |
| Industry | GicsIndustry? | GICS industry enum |
| RevenueTotal | double? | |
| GrossMargin | double? | |
| MarketCap | double? | USD market capitalization, sourced from FMP profile |
| AsOf | DateOnly? | |
| Cik | string? | SEC identifier |
| Type | CompanyType | PUBLIC (ticker-backed, real FMP/Yahoo data) / PRIVATE (no ticker, profile + estimated financials from web search); default PUBLIC |
| DeletedAt | DateTime? | Soft-delete timestamp |

Nav: `Financials` (ICollection<CompanyFinancial>) — the dated per-period history; Company itself keeps only the latest denormalized snapshot.

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
| DataSource | DataSource? | |
| DeletedAt | DateTime? | Soft-delete timestamp |
| Status | ContributionStatus | Review state; defaults to Approved (live). User contributions are Pending until a Manager rules on them |
| ContributedByUserId | string? | FK → AspNetUsers.Id (OnDelete SetNull, indexed); the user who proposed a pending row; null = system/admin write. Nav: `ContributedBy` |
| SupersedesId | long? | Self-reference (audit pointer, not a configured FK) to the live Approved row this pending edit would replace; null = brand-new addition |

Disclosed risk extracted from SEC Item 1A risk factors / Item 7A market risk. Mirrors RevenueSource/CostSource but has no money/percentage/counterparty — just Name + Scope + Note. Per-cell proof lives in SourceFieldReview (Relation = RISK).

### SourceFieldReview
| Property | Type | Notes |
|---|---|---|
| Id | long | PK |
| CompanyId | long | FK → Companies (analyzed company, denormalized) |
| Relation | RelationKind | COST / REVENUE / RISK discriminator |
| RevenueSourceId | long? | FK → RevenueSources; set iff Relation==REVENUE |
| CostSourceId | long? | FK → CostSources; set iff Relation==COST |
| CompanyRiskId | long? | FK → CompanyRisks; set iff Relation==RISK |
| Field | ReviewableField | Which cell this row proves |
| Endpoint | string | API endpoint that produced the proof |
| ReferencePointer | string | JSON path / text offset the user selected |
| ReferenceSnapshot | string | Literal proof text, frozen at reference time |
| ReferencedValue | string? | Value snapshot at reference time → staleness detection |
| FilingId | long? | FK → Filings; the filing this per-field proof was drawn from (null when proof came from Company Facts). Per-field, so one source cites several filings via its reviews. |
| Mark | int? | null=unreviewed, 0=fail, 1=pass |
| Rationale | string? | AI reason for the mark |
| ReviewedAt | DateTime? | |
| ReviewerModel | string? | Model id |
| DeletedAt | DateTime? | Soft-delete timestamp |

Constraints: check `CK_SourceFieldReview_OneSource` enforces exactly one of RevenueSourceId / CostSourceId / CompanyRiskId is set; unique indexes `(RevenueSourceId, Field)`, `(CostSourceId, Field)`, and `(CompanyRiskId, Field)` — one current reference per cell (NULLs don't collide across relation types).

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

Constraints: unique index on `AccessionNumber` — EDGAR accession numbers are globally unique, so there is one Filing row per filing, shared by every SourceFieldReview that cites it (upsert-by-accession). The filing link is per-field on SourceFieldReview, so one source can cite multiple filings across its reviews. Migration: `20260603210530_AddFiling`; link moved to SourceFieldReview in `20260603213908_MoveFilingLinkToReview`.

### AppUser
Extends ASP.NET Core Identity's `IdentityUser` (maps to **AspNetUsers**). Standard Identity columns (Id, UserName, NormalizedUserName, Email, NormalizedEmail, PasswordHash, SecurityStamp, etc.) come from the base class; only the added columns are listed below.

| Property | Type | Notes |
|---|---|---|
| ProfilePicturePath | string? | Web-relative path to the uploaded profile image on disk (e.g. `/uploads/profiles/{userId}/{guid}.png`); null = no picture |
| OriginalFileName | string? | Original uploaded file name |
| ContentType | string? | MIME content type of the upload |
| SizeBytes | long? | File size in bytes |
| UploadedAt | DateTime? | Upload timestamp |

Stores a single profile-picture file's metadata + path; the image bytes live on disk under `wwwroot/uploads/profiles/{userId}/`, not in the database. Note: AppUser does not use the soft-delete `DeletedAt` pattern (Identity-managed).

### UserApiKey
| Property | Type | Notes |
|---|---|---|
| UserId | string | PK + FK → AspNetUsers.Id (shared PK, 1:1) |
| DeepSeekKey | string? | Ciphertext (Data Protection API); null = not provided |
| FmpKey | string? | Ciphertext (Data Protection API); null = not provided |
| PerplexityKey | string? | Ciphertext (Data Protection API); null = not provided |

A user's own (bring-your-own) third-party API keys for the keyed external services — one row per user, shared primary key with AppUser. Each key column holds the value as **ciphertext** encrypted by the ASP.NET Data Protection API (`UserApiKeyProvider`), never the raw key, so a DB dump leaks nothing usable. Null = the user hasn't provided that key. Cascade-delete: keys vanish when the account is removed. No soft-delete `DeletedAt`. Migration: `20260614131546_AddUserApiKeys`.

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
Company ──────────────── RevenueSource      (1:N, FK CompanyId — owner)
Company ──────────────── CostSource         (1:N, FK CompanyId — owner)
Company ──────────────── CompanyRisk        (1:N, FK CompanyId — owner)
Company ──────────────── RevenueSource      (1:N, FK RelatedCompanyId — counterparty, nav: RevenueFromDependents)
Company ──────────────── CostSource         (1:N, FK RelatedCompanyId — counterparty, nav: CostFromDependents)
Company ◄──────────────► Event              (N:M, junction table)

Company ──────────────── SourceFieldReview  (1:N, FK CompanyId, Restrict)
RevenueSource ────────── SourceFieldReview  (1:N, FK RevenueSourceId nullable, Restrict)
CostSource ───────────── SourceFieldReview  (1:N, FK CostSourceId nullable, Restrict)
CompanyRisk ──────────── SourceFieldReview  (1:N, FK CompanyRiskId nullable, Restrict)

Company ──────────────── Filing             (1:N, FK CompanyId, Restrict)
Filing ───────────────── SourceFieldReview  (1:N, FK FilingId nullable, Restrict)

Event   ◄──────────────► TradeBloc          (N:M, junction table)

AppUser ──────────────── UserApiKey         (1:1, shared PK UserId, Cascade)

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
| GicsIndustry | SOFTWARE, AUTOMOBILES, SEMICONDUCTORS…, … (99 total) |
| EventType | EARNINGS, CENTRAL_BANK, MACRO_DATA, TRADE_DEAL, SANCTIONS, … |
| SourceType | CUSTOMER, SEGMENT, REGION, PRODUCT |
| CostBase | COGS, OPEX, TOTAL_COSTS |
| DataSource | EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB, FMP, YAHOO |
| FiscalPeriod | FY, Q1, Q2, Q3, Q4 |
| RelationKind | COST, REVENUE, RISK |
| ReviewableField | VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION, NOTE |
| RiskScope | MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL |
| ContributionStatus | Approved (=0, default/live), Pending, Rejected |
