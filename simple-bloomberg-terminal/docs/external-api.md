# External APIs

Catalog of public/free APIs that map onto this project's entities. EDGAR is implemented; the rest are candidates, ranked by fit to the existing data model. Each entry states **what entity it feeds** — an API earns a slot only if it populates a column we already render.

Existing implemented source: **SEC EDGAR** (see `reference_sec_edgar_api.md`). EDGAR covers **US filers only** — every gap below is framed against that limit.

`DataSource` enum today: `EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB`. New sources need new enum values (noted per API).

---

## Coverage gaps in the current model (why we need more sources)

| Gap | Cause | Filled by |
|---|---|---|
| `Country.GdpUsd`, `Country.Population`, all `GdpSnapshot` rows | hand-entered, no feed | **World Bank** |
| `Country.CurrencyCode`, `Region`, `Code` | hand-entered | **REST Countries** |
| Foreign-currency revenue → USD normalization | none | **Frankfurter (ECB)** |
| Non-US company fundamentals (Aramco, Samsung, Nestlé, LVMH, Reliance, Tencent — `Cik=null`) | EDGAR is US-only | **Finnhub** / **FMP** |
| `Event` rows of type `CENTRAL_BANK`, `MACRO_DATA` | hand-entered | **FRED** |
| `Event` rows of type news/`SANCTIONS`/`TRADE_DEAL` | hand-entered | **Marketaux** / **GDELT** |

---

## Tier 1 — best fit, implement first

### 1. World Bank Indicators API
**Source:** World Bank Open Data. **Key:** none. **Format:** JSON (`?format=json`).
**Why top pick:** half the model is macro (`Country`, `GdpSnapshot`, `CountryAdvantage/Challenge`, `TradeBloc`) and all of it is hand-entered today. World Bank fills it directly, keyed by ISO country code we already store in `Country.Code`.

**Feeds:**
| WB indicator | Code | Entity → field |
|---|---|---|
| GDP (current US$) | `NY.GDP.MKTP.CD` | `Country.GdpUsd` (latest) + one `GdpSnapshot` per year |
| Population, total | `SP.POP.TOTL` | `Country.Population` |
| Inflation, CPI % | `FP.CPI.TOTL.ZG` | `Event` (MACRO_DATA) or `Country.Notes` |
| Unemployment % | `SL.UEM.TOTL.ZS` | optional macro snapshot |

**Endpoints:**
- Series for one country: `GET https://api.worldbank.org/v2/country/{ISO}/indicator/{CODE}?format=json&per_page=100`
  - e.g. `.../country/US/indicator/NY.GDP.MKTP.CD?format=json` → GDP per year → maps straight to `GdpSnapshot{Year, GdpUsd}`.
- All countries: `.../country/all/indicator/SP.POP.TOTL?format=json`.

**Notes:** `Country.Code` is ISO-2/3 — WB accepts both. Response is `[meta, [datapoints]]` array; page via `per_page`. No rate-limit hassle. Suggested enum: `DataSource.WORLD_BANK`.

### 2. REST Countries
**Source:** restcountries.com (`/v3.1`). **Key:** none. **Format:** JSON.
**Feeds:** `Country` seed/enrich — `CurrencyCode` (from `currencies`), `Name`, `Region`, `Code`, population fallback.
**Endpoints:**
- By code: `GET https://restcountries.com/v3.1/alpha/{code}?fields=name,currencies,region,population,cca2,cca3`
- All: `GET https://restcountries.com/v3.1/all?fields=name,currencies,region,cca2`
**Notes:** static reference data — fetch once at seed time, no refresh loop needed. `currencies` is an object keyed by code → pull the key for `CurrencyCode`.

### 3. Frankfurter (ECB exchange rates)
**Source:** frankfurter.dev, ECB reference rates. **Key:** none. **Format:** JSON.
**Why:** EDGAR + foreign filers report in local currency. To put `RevenueSource.Value` / `CostSource.Value` on one USD axis (the graph compares across companies), convert at fetch time.
**Endpoints:**
- Latest: `GET https://api.frankfurter.dev/v1/latest?base=EUR&symbols=USD`
- Historical (match filing date): `GET https://api.frankfurter.dev/v1/{YYYY-MM-DD}?base=JPY&symbols=USD`
**Notes:** 30+ currencies, daily, history to 1999. No quota (rate-limited only). Use it as a converter helper, not a stored entity. Updated ~16:00 CET workdays — weekend dates return last working day.

---

## Tier 2 — fills the non-US company gap (needs free key)

### 4. Finnhub
**Source:** finnhub.io. **Key:** free, required. **Limit:** 60 req/min.
**Why:** EDGAR's hard wall is US-only. Finnhub gives **global** company fundamentals, earnings calendar, and company news — exactly the non-filer seed rows that currently 409.
**Feeds:**
| Finnhub endpoint | Entity → field |
|---|---|
| `/stock/metric?symbol=&metric=all` | `Company.RevenueTotal`, `GrossMargin`; `RevenueSource`/`CostSource` (tag `DataSource.FINNHUB`) |
| `/calendar/earnings` | `Event` (Type=EARNINGS, Date, EPS/revenue actuals → ImpactScore) |
| `/company-news?symbol=&from=&to=` | `Event` (news-driven, Description) |
**Notes:** mirror EDGAR's idempotent refresh — clear `DataSource==FINNHUB` rows, reinsert. Symbol-keyed (not CIK), so works for foreign tickers. New enum: `DataSource.FINNHUB`.

### 5. Financial Modeling Prep (FMP)
**Source:** financialmodelingprep.com. **Key:** free (250 req/day). **Why:** alternative/backup to Finnhub for global income statements + segment revenue — maps well to `RevenueSource{SourceType=SEGMENT}`.
**Feeds:** `/income-statement/{symbol}` → `RevenueSource`/`CostSource`; `/revenue-product-segmentation` → per-`PRODUCT` rows; `/revenue-geographic-segmentation` → per-`REGION` rows (matches our `SourceType` enum exactly).
**Notes:** segment endpoints are the standout — they fit `SourceType` 1:1. Lower daily cap than Finnhub.

### 6. Alpha Vantage
**Source:** alphavantage.co. **Key:** free (25 req/day — tight). **Feeds:** `OVERVIEW` → `Company` fundamentals; `INCOME_STATEMENT` → revenue/cost. **Notes:** 25/day cap makes it a fallback only, not a primary loop. 50+ technical indicators if price charts ever get added.

---

## Tier 3 — macro events & news (optional, enriches `Event`)

### 7. FRED (St. Louis Fed)
**Source:** fred.stlouisfed.org. **Key:** free, required (32-char). **Feeds:** `Event` (Type=CENTRAL_BANK / MACRO_DATA) — Fed funds rate (`FEDFUNDS`), CPI (`CPIAUCSL`), 10Y treasury (`DGS10`). Also a richer source for `Country.RiskRating` inputs.
**Endpoint:** `GET https://api.stlouisfed.org/fred/series/observations?series_id=FEDFUNDS&api_key=&file_type=json`
**Notes:** US-centric. Map a release/revision to an `Event` row dated at the observation date.

### 8. Marketaux
**Source:** marketaux.com. **Key:** free plan (no card). **Feeds:** `Event` (news) — 80+ markets, 5000+ sources, entity-tagged so you can attach news to a `Company`. Good for SANCTIONS / TRADE_DEAL event types.

### 9. GDELT
**Source:** gdeltproject.org. **Key:** none. **Feeds:** `Event` — global event/news stream, geo-tagged → attach to `Country`. Heavier/noisier; use only if a news firehose is wanted. No key, no quota.

---

## Implementation pattern (reuse EDGAR's shape)

Follow the existing `StockService` + `IStockApiClient` split for every new source — do not invent new layers (per project conventions):

- **Client** (`I{X}ApiClient` / `{X}ApiClient`): HTTP + deserialize only. Register with `AddHttpClient<>`. Set a `User-Agent`/key header where required.
- **Service logic**: map foreign payload → entities, persist via existing repos, fall back on failure. Reuse `IRevenueSourceRepository`/`ICostSourceRepository` clear-by-`DataSource` methods.
- **Idempotency**: clear rows where `DataSource == {SOURCE}`, reinsert. Never touch `MANUAL`. Events dedupe by `(CompanyId, Title, Date)`.
- **Failure**: unreachable → 503; not-found → 422; non-applicable (e.g. no symbol) → 409.
- **New `DataSource` enum values** to add: `WORLD_BANK`, `FINNHUB`, `FMP`, `FRED` (extend the existing `EDGAR, MANUAL, CLAUDE_ESTIMATED, OPENBB`).

---

## Quick reference — key & cost

| API | Key | Free limit | Best for |
|---|---|---|---|
| SEC EDGAR | none (User-Agent) | ~10 req/s | US filings *(implemented)* |
| World Bank | none | none | GDP / population / `GdpSnapshot` |
| REST Countries | none | none | Country metadata seed |
| Frankfurter | none | none (rate-limited) | FX → USD normalization |
| Finnhub | free | 60/min | global company fundamentals + earnings |
| FMP | free | 250/day | segment revenue (PRODUCT/REGION) |
| Alpha Vantage | free | 25/day | fallback fundamentals |
| FRED | free | generous | macro events (rates, CPI) |
| Marketaux | free | yes | financial news → events |
| GDELT | none | none | global event firehose |

---

## Sources
- [SEC EDGAR](https://www.sec.gov/edgar) *(implemented)*
- [World Bank Indicators API](https://datahelpdesk.worldbank.org/knowledgebase/articles/889392-about-the-indicators-api-documentation)
- [REST Countries](https://restcountries.com/)
- [Frankfurter](https://frankfurter.dev/)
- [Finnhub](https://finnhub.io/docs/api)
- [Financial Modeling Prep](https://site.financialmodelingprep.com/developer/docs)
- [Alpha Vantage](https://www.alphavantage.co/)
- [FRED API](https://fred.stlouisfed.org/docs/api/fred/)
- [Marketaux](https://www.marketaux.com/)
- [GDELT Project](https://www.gdeltproject.org/)
