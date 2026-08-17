# Entitetski model ekstrakcije podnesaka

Model je sužen na entitete koje izravno dodiruje ekstrakcija podnesaka jezičnim modelom. Izostavljeni
su entiteti pretraživanja weba, tržišnih podataka, indeksa, scenarija i geografije.

Konvencije: `Id` je primarni ključ tipa `long` (auto-increment); `DeletedAt` je oznaka mekog brisanja
(NULL = aktivan zapis); `?` označava nullable stupac.

## 1. Opseg

| Entitet | Tablica | Uloga u ekstrakciji |
|---|---|---|
| Filing | `Filings` | podnesak SEC EDGAR koji se čita |
| RevenueSource | `RevenueSources` | rezultat ekstrakcije — prihod |
| CostSource | `CostSources` | rezultat ekstrakcije — trošak |
| CompanyRisk | `CompanyRisks` | rezultat ekstrakcije — rizik |
| Company | `Companies` | vlasnik zapisa i protustranka |

`Company` je prikazan jer na njega pokazuju vanjski ključevi; njegovi atributi nisu rezultat
ekstrakcije. Stupac `ContributedByUserId` pokazuje na korisnika, entitet izvan opsega ovog modela.

Dokaz o izvoru bilježi se na samom zapisu trima stupcima: `Reference` (mjesto u dokumentu),
`Evidence` (doslovan citat) i `FilingId` (podnesak iz kojeg su preuzeti). Jedna ekstrakcija iz jednog
podneska stvara N zapisa s istim `FilingId`, a svaki nosi vlastiti `Reference` i `Evidence`.

---

## 2. Filing

| Stupac | Tip | Null | Napomena |
|---|---|---|---|
| Id | long | ne | PK |
| CompanyId | long | ne | FK → Company |
| AccessionNumber | string | ne | jedinstven; identitet podneska u EDGAR-u |
| Form | string | da | vrsta obrasca (10-K, 10-Q…) |
| FilingDate | DateTime | da | |
| PrimaryDocUrl | string | da | poveznica na primarni dokument |
| DeletedAt | DateTime | da | |

---

## 3. RevenueSource

| Stupac | Tip | Null | Napomena |
|---|---|---|---|
| Id | long | ne | PK |
| SourceType | SourceType | ne | šifrarnik klasifikacije |
| Name | string | ne | naziv stavke, segmenta ili proizvoda |
| Value | double | da | iznos u USD |
| Percentage | double | da | udio u ukupnom prihodu, 0–100 |
| Reference | string | da | mjesto u dokumentu (SEC Item, bilješka, podnaslov) |
| Evidence | string | da | doslovan citat iz podneska |
| FilingId | long | da | FK → Filing; NULL kad izvor nije dokument podneska |
| DataSource | DataSource | da | |
| CompanyId | long | ne | FK → Company (vlasnik) |
| RelatedCompanyId | long | da | FK → Company (protustranka) |
| Status | ContributionStatus | ne | zadano `Approved` (0) |
| ContributedByUserId | string | da | FK → korisnik |
| SupersedesId | long | da | zapis koji ovaj prijedlog zamjenjuje |
| DeletedAt | DateTime | da | |

---

## 4. CostSource

Struktura je istovjetna `RevenueSource`; razlikuje se samo šifrarnik klasifikacije.

| Stupac | Tip | Null | Napomena |
|---|---|---|---|
| Id | long | ne | PK |
| CostBase | CostBase | ne | šifrarnik klasifikacije |
| Name | string | ne | |
| Value | double | da | iznos u USD |
| Percentage | double | da | udio u ukupnom trošku, 0–100 |
| Reference | string | da | mjesto u dokumentu |
| Evidence | string | da | doslovan citat iz podneska |
| FilingId | long | da | FK → Filing |
| DataSource | DataSource | da | |
| CompanyId | long | ne | FK → Company (vlasnik) |
| RelatedCompanyId | long | da | FK → Company (protustranka) |
| Status | ContributionStatus | ne | |
| ContributedByUserId | string | da | FK → korisnik |
| SupersedesId | long | da | |
| DeletedAt | DateTime | da | |

---

## 5. CompanyRisk

| Stupac | Tip | Null | Napomena |
|---|---|---|---|
| Id | long | ne | PK |
| Scope | RiskScope | ne | šifrarnik klasifikacije |
| Name | string | ne | |
| Note | string | da | kratak opis rizika |
| Reference | string | da | mjesto u dokumentu (Item 1A / 7A) |
| Evidence | string | da | doslovan citat iz podneska |
| FilingId | long | da | FK → Filing |
| DataSource | DataSource | da | |
| CompanyId | long | ne | FK → Company |
| Status | ContributionStatus | ne | |
| ContributedByUserId | string | da | FK → korisnik |
| SupersedesId | long | da | |
| DeletedAt | DateTime | da | |

Rizik nema iznos, postotak ni protustranku.

---

## 6. Company

| Stupac | Tip | Null | Napomena |
|---|---|---|---|
| Id | long | ne | PK |
| Name | string | ne | |
| Cik | string | da | identifikator SEC EDGAR, veza prema podnescima |
| Type | CompanyType | ne | zadano `PUBLIC` |
| CountryId | long | ne | FK → Country |
| Sector | Sector | da | NULL = nerazvrstano |
| ClassifyStatus | ClassifyStatus | ne | zadano `Pending` |
| ClassificationLocked | bool | ne | ručno razvrstavanje koje automatika ne prepisuje |
| FmpIndustry | string | da | izvorna oznaka djelatnosti dobavljača podataka |
| GicsSubIndustry | GicsSubIndustry | da | najfinija razina GICS |
| Industry | GicsIndustry | da | denormalizirano iz `GicsSubIndustry` |
| RevenueTotal | double | da | |
| GrossMargin | double | da | udio 0–1 |
| MarketCap | double | da | |
| AsOf | DateOnly | da | |
| Notes | string | da | |
| DeletedAt | DateTime | da | |

---

## 7. Veze

| Od | Prema | Preko | Kardinalnost | Ponašanje pri brisanju |
|---|---|---|---|---|
| Company | RevenueSource | CompanyId | 1:N | Cascade |
| Company | CostSource | CompanyId | 1:N | Cascade |
| Company | CompanyRisk | CompanyId | 1:N | Cascade |
| Company | RevenueSource | RelatedCompanyId | 1:N | NoAction |
| Company | CostSource | RelatedCompanyId | 1:N | NoAction |
| Company | Filing | CompanyId | 1:N | Restrict |
| Filing | RevenueSource | FilingId | 1:N | Restrict |
| Filing | CostSource | FilingId | 1:N | Restrict |
| Filing | CompanyRisk | FilingId | 1:N | Restrict |

Razlika slijedi iz obaveznosti vanjskog ključa: `CompanyId` je obavezan pa je veza `Cascade`,
a `RelatedCompanyId` je neobavezan pa brisanje protustranke ne dira zapis.

Jedna firma ima više izvora prihoda, troška i rizika. Jedan podnesak potkrepljuje više zapisa — po
jedan za svako mjesto u dokumentu na kojem je nešto pronađeno.

---

## 8. Ograničenja i indeksi

| Tablica | Stupci | Vrsta | Svrha |
|---|---|---|---|
| Filings | AccessionNumber | jedinstveni | jedan zapis po podnesku |
| RevenueSources | FilingId | obični | vanjski ključ |
| CostSources | FilingId | obični | vanjski ključ |
| CompanyRisks | FilingId | obični | vanjski ključ |

`Restrict` na vezi prema podnesku znači da se citirani podnesak ne može tvrdo obrisati ispod zapisa
koji ga citira.

---

## 9. Šifrarnici

| Šifrarnik | Vrijednosti |
|---|---|
| SourceType | CUSTOMER, SEGMENT, REGION, PRODUCT |
| CostBase | COGS, OPEX, TOTAL_COSTS |
| RiskScope | MACROECONOMIC, INDUSTRY, BUSINESS, LEGAL_REGULATORY, FINANCIAL, GENERAL |
| ContributionStatus | Approved (0), Pending, Rejected |
| DataSource | EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB, FMP, YAHOO |

Šifrarnici entiteta `Company` (`CompanyType`, `ClassifyStatus`, `Sector`, `GicsIndustry`,
`GicsSubIndustry`) pripadaju razvrstavanju tvrtki i nisu dio ekstrakcije.
