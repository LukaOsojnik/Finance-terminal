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
Company ──────────────── RevenueSource      (1:N, FK RelatedCompanyId — counterparty, nav: RevenueFromDependents)
Company ──────────────── CostSource         (1:N, FK RelatedCompanyId — counterparty, nav: CostFromDependents)
Company ◄──────────────► Event              (N:M, junction table)

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
