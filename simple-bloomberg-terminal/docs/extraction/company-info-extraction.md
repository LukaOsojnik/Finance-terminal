# Company info extraction — shallow vs depth FMP calling & company creation

Status: **implemented.** The map of *every* path that creates or enriches a company, what it costs in
FMP calls, and which tier (shallow / depth) it runs. Companion to
[`company-industry-extraction.md`](./company-industry-extraction.md), which details the classification
step itself.

## The two cost tiers

Every company has two qualities of data, at very different FMP costs:

| Tier | What | FMP cost | Source |
|------|------|----------|--------|
| **Shallow** | Identity + classification: name, CIK, country, sector, market cap, **industry label** → GICS sub-industry → Industry | **1 call** — the FMP *profile* (+ 1 cheap LLM, cache-deduped) | `GetProfileAsync` |
| **Depth** | Dated financial **history**: income / ratios / balance / cash-flow, annual + quarterly | **up to ~8 calls** — the four statement endpoints × two granularities (Yahoo fallback when FMP is gated) | `CompanyFinancialsService.BuildAsync` |

**The principle:** one FMP **profile** call carries everything needed to *create and classify* a company.
Financials is a separate, much more expensive **depth** extraction. So when call budget matters (bulk),
do **shallow first** and defer / skip depth; do **full** (shallow + depth) only when depth is actually
wanted and the volume is small.

> Quota note: a profile fetch is one call; the financials fetch is several. The FMP free tier has a daily
> quota, so the shallow/depth split is what lets a 200-member index get *classified* today even if
> *financials* won't finish until tomorrow.

---

## When each tier runs

### Shallow-first (bulk — classify now, depth later or never)

- **Index import — provisioning new members** (`CreateFromProfileAsync`). 200+ companies, so cost matters:
  one profile call per new member, store the label, **no depth, no LLM** inline. Industry is resolved in
  the background Enrich step (below). Matched (already-in-DB) members make **zero** calls — they're only
  linked, and picked up later by the industry backfill.
- **Resolve Industries backfill** (`BackfillIndustriesAsync`). We're backfilling *industry*, so financials
  is irrelevant: per company, one profile call to get the label (if absent) + one LLM call. Stops fetching
  labels on the FMP 429 but keeps resolving rows whose label is already known. No depth at all.

The index-import **Enrich** step (`EnrichAsync`, background, right after provisioning) is where the two
tiers meet, **shallow-first**: it resolves each new member's industry from the already-stored label
(0 FMP) **before** attempting financials, and a financials 429 stops only the depth fetch — classification
still completes for every member. So "index adding" is *staged full*: shallow up front, depth trailing and
degradable.

### Full (shallow + depth — single adds, or depth explicitly wanted)

- **New Company by ticker** (`BuildFromTickerAsync`). A deliberate single add: profile **+** income for the
  form's revenue/margin prefill, industry resolved at fetch, full financial history pulled on save. Depth
  is wanted here because the user is inspecting one company.
- **Counterparty link from revenue/cost/risk extraction** (`GetOrCreateCounterpartyAsync`). When an
  extraction agent links a supplier/customer by ticker, that counterparty is created **full**: profile +
  income + dated financials, so the graph node is complete. (Best-effort: any FMP failure falls back to a
  minimal stub; the link never blocks.)
- **Backfill financials** (`BackfillFinancialsAsync`). The explicit depth sweep: dated history for every
  company that lacks FMP financials. Stops cleanly on the daily 429 to resume tomorrow.

### FMP-free (no tiering — web-search creation)

- **Private-company discovery & re-discovery** (`BuildPrivateAsync`, via Perplexity sonar). No ticker, so
  **no FMP at all**: identity + revenue + industry label come from the web-search profile; the sonar
  industry string is resolved to a GICS sub-industry the same way an FMP label is. Financials are the
  AI-estimated figure stored as a single `CLAUDE_ESTIMATED` row, not an FMP history.

---

## Every company-touching path at a glance

| Path | Trigger | FMP calls | Tier |
|------|---------|-----------|------|
| `CreateFromProfileAsync` | Index import — **new** member | profile = **1** | shallow |
| (index match) | Index import — **existing** member | **0** (linked only) | none → backfill |
| `EnrichAsync` | Background, post-import | industry **0** FMP + financials ~8 | shallow-first, then depth |
| `BackfillIndustriesAsync` | "Resolve Industries" button | profile **1** (if no label) + LLM | shallow |
| `BackfillFinancialsAsync` | "Backfill Financials" button | ~8 / company | depth (+ shallow via kernel) |
| `BuildFromTickerAsync` | New Company form — Fetch | profile + income + financials | full |
| `GetOrCreateCounterpartyAsync` | Revenue/cost/risk extraction link | profile + income + financials | full |
| `BuildPrivateAsync` | Private discovery / re-discovery | **0** (Perplexity) | FMP-free |
| (manual New Company) | Form, no ticker | **0** | none (user-entered) |

---

## Why the split is structural, not incidental

- **Classification must not be held hostage by depth.** Industry needs only the profile call, so it is
  resolved independently of financials everywhere; an FMP quota exhaustion stops depth fetching but never
  blocks classifying the rest. (This was a real bug in the first cut of `EnrichAsync`, where a financials
  429 `break` skipped the cheap industry step for every remaining member.)
- **Labels are persisted, so depth can come later for free-ish.** `Company.FmpIndustry` is stored raw at
  the shallow step, so a later run re-resolves the industry with **no** FMP call, and the depth backfill
  can run on its own schedule without re-deriving identity.
- **Bulk defers depth; single adds take it eagerly.** The same building blocks (`BuildFromTickerAsync`,
  `CompanyFinancialsService.BuildAsync`, `ResolveSubIndustryAsync`) compose differently by volume: hundreds
  of index members get shallow-first + background depth; one hand-added ticker gets everything at once.

---

## Code map

The files and the role each plays in the shallow/depth pipeline. (Paths relative to the project root.)

### Data

| File | Role |
|------|------|
| `Models/Entities/Company.cs` | Carries `FmpIndustry` (raw label), `GicsSubIndustry` (163), `Industry` (74 rollup). |
| `Models/Entities/FmpIndustryMapping.cs` | The learned `label → sub-industry` cache row (`Label` unique). |
| `Models/Enums/GicsSubIndustry.cs` | The 163 sub-industries + `GetIndustry()`/`GetSector()` rollup. |
| `Models/Enums/GicsIndustry.cs` | The 74 industries (rollup target) + `GetSector()`. |
| `Data/AppDbContext.cs` | `DbSet<FmpIndustryMapping>` + unique index on `Label`. |
| `Migrations/…AddGicsSubIndustryAndFmpLabelCache.cs` | Adds the two `Company` columns + the cache table. |

### Shallow — classification (1 profile call + cheap LLM)

| File | Role |
|------|------|
| `Services/FmpMapper.cs` | Maps an FMP profile → create-model; stores the raw label, maps the sector. **No** label→industry dict (retired). |
| `Services/IndustryClassifier.cs` | `ResolveSubIndustryAsync` — cache → cheap (`fast`) LLM, sector-constrained → persist. Logs the reason on a "no fit". |
| `Repositories/FmpIndustryMappingRepository.cs` | Get/Set on the cache, keyed by normalized label. |

### Depth — financials (the expensive sweep)

| File | Role |
|------|------|
| `Services/CompanyFinancialsService.cs` | `BuildAsync` — the four FMP statement endpoints × annual+quarter (Yahoo fallback). The ~8-call cost. |

### Orchestration — who calls shallow vs depth

| File | Role |
|------|------|
| `Services/CompanyProvisioningService.cs` | All the flows: `CreateFromProfileAsync` (shallow), `EnrichAsync` (shallow-first then depth), `BackfillFinancialsAsync` (depth), `BackfillIndustriesAsync` (self-contained shallow), `GetOrCreateCounterpartyAsync` / `BuildPrivateAsync`. |
| `Services/TickerProfileEnricher.cs` | The New-Company shared kernel: profile → model, resolve sub-industry, Yahoo fallback. |
| `Services/CompanyMapper.cs` | `ToEntity` (birth) / `Apply` (merge) — the one place the create-model → entity field list lives; both carry all three industry fields. |

### Edges — controller, view, DI

| File | Role |
|------|------|
| `Controllers/CompaniesController.cs` | `Fetch`/`Create` (single add), `BackfillFinancials` + `BackfillIndustries` (detached starters), `BackfillStatus`/`BackfillCancel`, `RunBackfillDetached`. |
| `Controllers/IndicesController.cs` | Index import → `CreateFromProfileAsync` (provision) then `EnrichAsync` in the background. |
| `Services/BackfillJobStore.cs` | Detached-job state: progress lines + `CancellationTokenSource` (+ `SyncProgress<T>`). |
| `Views/Companies/Index.cshtml` | The two backfill buttons + live-progress/cancel modal JS. |
| `Views/Companies/Create.cshtml` | New-Company form; hidden `FmpIndustry`/`GicsSubIndustry` round-trip. |
| `Program.cs` | Registers the cache repo (scoped), classifier (scoped), `BackfillJobStore` (singleton). |
