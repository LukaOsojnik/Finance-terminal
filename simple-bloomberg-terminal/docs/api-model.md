# API → Model: sourcing the company-relationship graph

This doc answers one question: **which APIs best populate the company-to-company relationship edges** — who a company's biggest customers (revenue) and biggest cost sources / suppliers (cost) are.

> Example target: *Apple's biggest cost source is TSMC.* That is one `CostSource` row:
> `{ CompanyId = Apple, RelatedCompanyId = TSMC, CostBase = COGS, Name = "TSMC chip fabrication", Value/Percentage, DataSource = ? }`

Companion to `external-api.md` (which covers the macro/Country side). Here the focus is narrow: the `RelatedCompanyId` edges.

---

## The exact model target

The relationship graph lives in **two FK columns**, not in `Company` itself:

| Edge | Entity + column | Meaning | Hub it renders in |
|---|---|---|---|
| who buys from me | `RevenueSource.RelatedCompanyId` | counterparty = customer | RELATED COMPANIES / REVENUE |
| who I pay / depends on | `CostSource.RelatedCompanyId` | counterparty = supplier | RELATED COMPANIES / COST |

Reverse navs already exist: `Company.RevenueFromDependents`, `Company.CostFromDependents`. So Apple→TSMC as a *cost* edge automatically shows up on TSMC's page as *revenue from a dependent*. **One row, both directions.**

A row only joins the RELATED-COMPANIES graph when `RelatedCompanyId` is non-null **and** that counterparty exists as a `Company`. EDGAR can't do this — its aggregates always set `RelatedCompanyId = null` (see `external-api.md`). So this is genuinely new sourcing work.

---

## Hard truth: no free *structured* supplier/customer API

Company-to-company relationship data is the premium product of the data industry. The structured feeds exist but are **all paid**:

| Provider | What it gives | Cost |
|---|---|---|
| FactSet Supply Chain API | relationships classified into customer / supplier / partner / competitor (13 sub-types) | enterprise paid |
| Trademo | buyer↔supplier strength from customs/trade data, 190+ countries | paid |
| AroundDeal Company Suppliers API | structured supplier list from public data | paid |
| Bloomberg SPLC / Refinitiv | the gold standard | very paid |

Free structured options (Open Supply Hub) cover **factory/manufacturing** supply chains, not financial customer-concentration — wrong granularity for our model.

**Conclusion:** for a free build, the relationship edges cannot be *fetched ready-made*. They must be **extracted from text**. That matches your instinct ("paragraphs which I will give AI to parse") — and the model already has the enum value for it: `DataSource.CLAUDE_ESTIMATED`.

---

## Recommended architecture: text source → LLM extraction → edge rows

```
fetch text (10-K / news / encyclopedia)
   → send to Claude with the company + known peer list
   → Claude returns {counterparty, direction, rationale, est. %}
   → resolve counterparty name → existing Company.Id (skip if unknown)
   → write RevenueSource/CostSource row, RelatedCompanyId set, DataSource = CLAUDE_ESTIMATED
```

Two distinct DataSource meanings to keep clean:
- `EDGAR` / `FINNHUB` → numbers pulled verbatim from a feed, `RelatedCompanyId = null`.
- `CLAUDE_ESTIMATED` → relationship + magnitude inferred from text, `RelatedCompanyId` set. **This is the tag for every edge in this doc.**

---

## Ranked text sources for relationship extraction

### 1. SEC EDGAR — 10-K narrative (best signal, free, you already integrate EDGAR)
US-listed companies are **legally required** to disclose any customer that is >10% of revenue (Reg S-K / FASB customer-concentration rules). These land in the 10-K's *Business* and *MD&A* sections and the revenue-concentration footnote — as prose, e.g. *"During fiscal 2025, no single customer accounted for more than 10%…"* or named customers/suppliers in risk factors.

**Two EDGAR endpoints, two jobs:**

- **Full-text search** (`efts.sec.gov`) — discovery: find which filings even mention a peer.
  - Base: `GET https://efts.sec.gov/LATEST/search-index`
  - Params: `q` (exact phrase in quotes, boolean ok), `forms` (`10-K`), `ciks`, `dateRange=custom&startdt=&enddt=`, `from`, `size` (max 100).
  - Example: `?q="TSMC"&forms=10-K&size=50` → filings naming TSMC.
  - Response: `hits.hits[]` with `_id` = accession number, entity, form, filing date.
  - Headers: `User-Agent: Name email` (same as existing EDGAR client). Limit: 10 req/s.
- **Document fetch** — pull the actual 10-K text via the accession number (Archives path), slice the Business / Risk Factors / MD&A sections, feed to Claude for extraction.

**Why #1:** free, authoritative, already in your stack, and the disclosures are *mandated* so the signal is real. Limit: US filers only (matches EDGAR's existing limit; non-US covered below).

### 2. Wikidata SPARQL (free, no key — structured but sparse)
Endpoint: `GET https://query.wikidata.org/sparql?format=json&query=...`.
Has structured corporate relations — parent/subsidiary (P749/P355), owner, **but supplier/customer links are rare and inconsistent**. Best use: **name normalization** — resolve "TSMC" / "Taiwan Semiconductor" / "台積電" to one canonical entity before matching to a `Company.Id`. Treat as a *resolver*, not a relationship source.

### 3. Financial news — Marketaux / Finnhub company-news (free tier)
News articles routinely name trading partners ("Apple cuts orders at key supplier TSMC"). Entity-tagged feeds (Marketaux) let you pull articles already linked to a `Company`, then AI-extract the counterparty. **This is the path for non-US companies** that have no 10-K. Noisier than filings → keep `DataSource = CLAUDE_ESTIMATED` and store the source URL in `Name`/`Description`.

### 4. Wikipedia / DBpedia article text (free, no key)
A company's Wikipedia "customers"/"suppliers"/"partners" prose is a cheap, broad-coverage paragraph source for the AI-parse path — good global coverage, lower reliability. Fallback when no filing and thin news.

---

## Source-to-edge fit summary

| Source | Key | Coverage | Edge quality | Role |
|---|---|---|---|---|
| EDGAR 10-K text | none (UA) | US filers | high (mandated) | **primary** revenue & cost edges |
| EDGAR full-text search | none (UA) | US filers | n/a (discovery) | find which filings name a peer |
| Marketaux / Finnhub news | free | global | medium | non-US edges, recent changes |
| Wikipedia / DBpedia | none | global | low–medium | broad fallback paragraphs |
| Wikidata SPARQL | none | global | n/a | **name → Company.Id resolver** |
| FactSet / Trademo / Bloomberg | paid | global | high | reference only — out of scope |

---

## Concrete pipeline (reuse the EDGAR `StockService` shape)

Per project conventions — no new layers, mirror EDGAR's client/service/repo split and idempotency.

1. **Discovery** — `IFullTextSearchClient` over `efts.sec.gov`: for company X, search its 10-K for each known peer name (or pull the full Business/MD&A section once).
2. **Fetch** — get the filing document text (existing EDGAR client + Archives path); slice the relevant sections.
3. **Extract** — pass `{companyName, sectionText, candidatePeers: [existing Company names]}` to Claude. Return strict JSON: `[{counterparty, direction: REVENUE|COST, costBase|sourceType, estPercentage, quote}]`. Constrain to `candidatePeers` so unknown names are dropped, not invented.
4. **Resolve** — map `counterparty` → `Company.Id` (Wikidata/alias table for name variants). Skip if no match — never auto-create counterparties.
5. **Persist** — write `RevenueSource`/`CostSource` with `RelatedCompanyId` set, `DataSource = CLAUDE_ESTIMATED`, the source `quote` in `Name`/`Description`.
6. **Idempotency** — clear `DataSource == CLAUDE_ESTIMATED` rows for the company, reinsert (same pattern as EDGAR's `DataSource == EDGAR` wipe). Never touch `MANUAL`.

**Endpoint shape** (mirrors existing `POST /api/stock/refresh/{companyId}`):
`POST /api/relationships/refresh/{companyId}` → discover + fetch + AI-extract + persist → returns `CompanyDto` with nested counterparty edges.

---

## Verdict

- **Want it free:** EDGAR 10-K text + Claude extraction is the only honest route to the `RelatedCompanyId` graph. Apple→TSMC is literally in Apple's filings and supplier risk-factor prose.
- **The data is text, not a feed** — so the "give AI paragraphs to parse" plan isn't a workaround, it's the correct design. The `CLAUDE_ESTIMATED` enum value confirms the model was built expecting it.
- **Structured shortcut exists but costs money** (FactSet/Trademo/Bloomberg) — note it, skip it for now.

---

## Sources
- [SEC EDGAR Full-Text Search API guide](https://tldrfiling.com/blog/sec-edgar-full-text-search-api)
- [SEC EDGAR Full Text Search](https://www.sec.gov/edgar/search/)
- [FactSet Supply Chain API](https://developer.factset.com/api-catalog/factset-supply-chain-api)
- [Trademo Supply Chain API](https://www.trademo.com/apis)
- [AroundDeal Company Suppliers API](https://www.arounddeal.com/api-matrix/company-suppliers-api)
- [Open Supply Hub API](https://info.opensupplyhub.org/api)
- [Wikidata SPARQL query service](https://query.wikidata.org/)
- [Marketaux news API](https://www.marketaux.com/)
- [Finnhub company news](https://finnhub.io/docs/api)
