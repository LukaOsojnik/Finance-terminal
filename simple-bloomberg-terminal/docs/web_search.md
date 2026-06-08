# Web Search — counterparty discovery via Perplexity

SEC filings rarely name the *companies* on the other side of a cost or revenue line — segment/region/product breakdowns dominate, and supplier/customer names are usually absent or redacted. So the **Discover** feature fills the gap with a web search: it asks **Perplexity's `sonar-pro`** model for the named suppliers and customers behind a company's business segments, and turns the answer into reviewable suggestions the user links into the graph.

Companion to `extraction-pipeline.md` (where filing-based sources come from) and `external-api.md` (the other external data sources). Discovery produces the same `RevenueSource` / `CostSource` rows with a `RelatedCompanyId`, so the results land on the existing hub-and-spoke graph as "RELATED COMPANIES".

---

## Why Perplexity, and why `sonar-pro`

Perplexity's API is **OpenAI-compatible** (`POST /chat/completions`), so it reuses the same request/response shape as the DeepSeek client — the only differences are the base URL, key and model from the `Perplexity` config section. Unlike a plain LLM, `sonar` models **search the web themselves** and return an already-grounded answer plus a `citations` array. No separate search API (Tavily, Brave, SerpAPI) is wired.

**Multi-query, Perplexity-style.** A single broad call shares one citation pool, so links cluster. Instead Discover runs in two phases — a cheap **planner** call decomposes the company + its segments into several focused, distinct sub-queries (a different segment / product line / region / tier each), then **each sub-query is its own grounded search** with its own citations. Diverse queries → diverse sources. Results **stream** back as they land (NDJSON), so the page shows a live feed instead of one long wait.

Model choice is deliberate:

| Model | Used? | Why |
|---|---|---|
| `sonar` (base) | no | Returned generic aggregator links (csimarket, wikis). |
| **`sonar-pro` + `search_context_size: high`** | **yes** | Reliable, fast, primary sources (SEC, IR, industry press); a real `source_url` on every item. |
| `sonar-reasoning-pro` | no (tried, removed) | The `<think>` trace eats the token budget unpredictably → truncation/empty results, and emits **no per-item citations** → zero links. Reasoning models optimise for think-then-terse-answer, not cited structured extraction. |

`search_context_size: "high"` controls how much web content the model reads before answering (used on the search calls; the planner uses `"low"` since it only needs the company's real segment names). Cost is ~2–3× base `sonar` per high call, and a Discover click now fires one planner + N search calls (see the flow below).

---

## Where it lives

Two buttons on the **company Details page** (`Views/Companies/Details.cshtml`), one per section:

```
// REVENUE SOURCES
  CUSTOMERS BY SEGMENT (PERPLEXITY SONAR)            [ DISCOVER CUSTOMERS → ]
  └─ results render here, grouped by segment

// COST SOURCES
  SUPPLIERS BY SEGMENT (PERPLEXITY SONAR)            [ DISCOVER SUPPLIERS → ]
  └─ results render here, grouped by segment
```

- **Revenue** side discovers **customers** (companies that buy) → become `RevenueSource` rows (`SourceType.CUSTOMER`).
- **Cost** side discovers **suppliers** (companies it buys from) → become `CostSource` rows (`CostBase.COGS`).

---

## The flow

```
[DISCOVER button]
   │  JS collects this section's segment names from the rendered table rows
   ▼
POST /extraction/discover-related   { companyId, side, segments[] }
   │  ExtractionController.DiscoverRelated — streams NDJSON
   ▼
ICounterpartyDiscovery.DiscoverAsync(...)  → IAsyncEnumerable<DiscoveryEvent>
   │  CounterpartyDiscoveryService
   │
   ├─ PlanQueriesAsync   Perplexity (sonar-pro, context: low, ~700 tok)
   │     └─ {"queries":[…]}  →  emit  {t:"plan", queries[]}
   │
   └─ for each query (sequential, so the feed updates per query):
         emit {t:"searching", query}
         SearchAsync(query)   Perplexity (sonar-pro, context: high, 4000 tok)
            └─ content JSON string + citations[]  →  Parse()  →  suggestions
         dedupe by company name across queries
         emit {t:"result", query, items[]}
   ▼
each NDJSON line streamed to the page  →  live feed + results grouped by segment
   ▼
[LINK button per row]
   ▼
POST /extraction/link-counterparty
   │  reuse-or-create the counterparty Company, create the source row, save the citation
   ▼
graph edge (RELATED COMPANIES hub)
```

Nothing is persisted until the user clicks **LINK** on a row — discovery itself is read-only. Each Discover click is now **1 planner call + N search calls** (N = planned queries, capped at `MaxQueries = 6`), so it costs more than the old single call but returns far more diverse sources.

---

## Segmented vs company-wide

A "segment" is a real business segment (e.g. "iPhone", "AWS"), **not** a classification enum. The page only treats a source row as a segment if it is **not** an EDGAR period-total and **not** itself a linked counterparty:

```razor
data-segment="@(r.RelatedCompanyId == null && r.DataSource != DataSource.EDGAR ? r.Name : null)"
```

(When the value is `null`, Razor omits the attribute, so the JS selector `tr[data-segment]` skips that row.)

| Mode | Trigger | Prompt |
|---|---|---|
| **Segmented** | the section has real segment rows | "Here are the company's segments: …. For each, list its named customers/suppliers." |
| **Company-wide** | no segments on record yet | "Identify the company's main segments yourself, then for each list its named customers/suppliers." |

So Discover works even on a company with **zero sources** — the model identifies the segments itself.

**Recency:** every prompt scopes results to the **last 5 years** (`RecencyYears`), computed from the current year, so stale relationships (e.g. a supplier that ended in 2017) are excluded.

---

## How the answer becomes objects

The model returns a JSON *string* inside the HTTP envelope. Two trust levels:

1. **Envelope** (`{ "choices": […], "citations": […] }`) — reliable; parsed with `JsonDocument` to pull `content` and the `citations` array.
2. **`content`** — the model's JSON, untrusted (may carry prose/fences). `Parse()` scrapes from the first `{` to the last `}`, then hand-maps each `counterparties[]` element into a `CounterpartySuggestion`.

**Truncation safety:** company-wide answers list many companies and can hit the `max_tokens` cap (`finish_reason=length`), cutting the array mid-stream. If the slice won't parse, `Parse` salvages every complete object by appending `]}` to close the array.

Each `CounterpartySuggestion` carries: `Name`, `Side`, `Segment`, `Classification`, `Note`, `SourceUrl`, `CountryCode`, `Sector`, `Ticker`, and `ExistingCompanyId` (set when the name fuzzy-matches a company already in the DB).

---

## Source links (citations)

`sonar-pro` cites sources in two shapes, both resolved by `ResolveSource`:

- a real `"https://…"` string in `source_url` → used directly;
- a `[n]` marker (in `source_url` or embedded in the `note` prose) → resolved against the response's 1-based `citations[]` array.

On **LINK**, the resolved URL is saved as a `SourceFieldReview` on the new row's `RELATED_COMPANY` field (`Endpoint = "Perplexity sonar"`, `ReferencePointer = <url>`, no filing). It then shows as a `web ↗` link in the row's proof cell on the Details page.

---

## Linking — reuse or create via FMP

When the user confirms a row (`ExtractionController.LinkCounterparty` → `GetOrCreateCompanyAsync`):

1. **Reuse:** `ICompanyRepository.MatchByName` matches after stripping corporate suffixes (Corp/Inc/Ltd…), so sonar's "Microsoft Corporation" reuses an existing "Microsoft".
2. **Create via ticker:** if no match and sonar returned a `ticker`, run the same **FMP-by-ticker** pipeline the New Company form uses (`FmpMapper.ToCreateModel` → real sector/industry/CIK/revenue). A second `MatchByName` on FMP's canonical name avoids a duplicate.
3. **Fallback:** no ticker / FMP miss → a minimal company seeded from sonar's country/sector (or the owner's).

The new row's `RelatedCompanyId` points at that company, feeding the graph's RELATED COMPANIES hub. The discovered row has `Value = null` (the web gives no figure) and `DataSource = MANUAL`.

> **FMP free-tier note:** most counterparty tickers are foreign (`.TW/.SZ/.HK`), which FMP gates — those fall back to the minimal company; US tickers (e.g. `GLW`) enrich fully. See `external-api.md`.

---

## Configuration

```jsonc
// appsettings.json
"Perplexity": {
  "BaseUrl": "https://api.perplexity.ai",
  "ApiKey":  "pplx-…",
  "Model":   "sonar-pro"
}
```

Registered as a typed `HttpClient` in `Program.cs`:

```csharp
builder.Services.AddHttpClient<ICounterpartyDiscovery, CounterpartyDiscoveryService>();
```

---

## Key files

| File | Role |
|---|---|
| `Services/CounterpartyDiscoveryService.cs` | Builds the prompt, calls Perplexity, parses the answer + citations. |
| `Services/ICounterpartyDiscovery.cs` | The discovery boundary. |
| `Controllers/ExtractionController.cs` | `discover-related` (suggest) + `link-counterparty` (persist) endpoints; `GetOrCreateCompanyAsync`. |
| `Models/ViewModels/ExtractionViewModels.cs` | `CounterpartySuggestion`, `DiscoverCounterpartiesRequest`, `LinkCounterpartyRequest`. |
| `Repositories/CompanyRepository.cs` | `MatchByName` (suffix-stripping fuzzy match). |
| `Views/Companies/Details.cshtml` | The two DISCOVER buttons, grouped results renderer, link calls. |
| `CompanyGraphConverter.cs` | Turns `RelatedCompanyId` into the RELATED COMPANIES graph hub. |

---

## Endpoints

| Method · Route | Purpose | Persists? |
|---|---|---|
| `POST /extraction/discover-related` | Plan sub-queries, then search each; **streams NDJSON** (`{t:"plan"\|"searching"\|"result"\|"error"}`) for a live feed. Body: `{ companyId, side, segments[] }`. | No |
| `POST /extraction/link-counterparty` | Confirm one: reuse/create the company, create the source row, save the citation. | Yes |
