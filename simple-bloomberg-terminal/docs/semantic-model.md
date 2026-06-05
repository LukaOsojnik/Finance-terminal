# Semantic DB Model

> Soft-delete pattern: every entity has a nullable `DeletedAt` (`DateTime?`); rows with `DeletedAt != null` are hidden from list/detail queries — repositories filter `Where(e => e.DeletedAt == null)` on every read. Migration: `20260516183211_AddSoftDeleteToAllEntities`.

## Tables / Classes

| Table | Class | Role |
|---|---|---|
| Countries | Country | Core geographic/economic entity |
| Companies | Company | Corporation linked to a country |
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
| AsOf | DateOnly? | |
| Cik | string? | SEC identifier |
| DeletedAt | DateTime? | Soft-delete timestamp |

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
```

---

## Enums

| Enum | Values (abbreviated) |
|---|---|
| Sector | ENERGY, MATERIALS, FINANCIALS, INFORMATION_TECHNOLOGY, … (11 total) |
| GicsIndustry | SOFTWARE, AUTOMOBILES, SEMICONDUCTORS…, … (99 total) |
| EventType | EARNINGS, CENTRAL_BANK, MACRO_DATA, TRADE_DEAL, SANCTIONS, … |
| SourceType | CUSTOMER, SEGMENT, REGION, PRODUCT |
| CostBase | COGS, OPEX, TOTAL_COSTS |
| DataSource | EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB |
| RelationKind | COST, REVENUE, RISK |
| ReviewableField | VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION, NOTE |
| RiskScope | MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL |
