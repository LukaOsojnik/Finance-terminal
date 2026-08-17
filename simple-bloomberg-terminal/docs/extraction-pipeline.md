# Extraction Pipeline — human entry with referenced provenance

A pipeline for gathering **cost & revenue source** data with provenance: every source row carries the
proof it was drawn from — one `Reference` (where in the document), one `Evidence` (the verbatim
substring) and one `FilingId` (the **`Filing`** it came from), so the row connects to its document on
the graph.

> An AI "phase 2" analyst-reviewer that graded each cell 0/1 was designed here and never built. The
> per-field `SourceFieldReview` table it needed was dropped in migration
> `20260814074704_CollapseProofOntoSourceRows`; its grading columns (`Mark`, `Rationale`,
> `ReviewedAt`, `ReviewerModel`) were never written by any code path. Proof is now one pair per row.

Scope: `RevenueSource` and `CostSource` first. Companion to `api-model.md` (where the data comes from), `external-api.md` (the macro side), and `web_search.md` (discovering counterparty companies via Perplexity when filings don't name them).

---

## The two companies on every row

A cost/revenue source is an edge between two companies — this is already in the model, no new columns:

| Role | Column on `RevenueSource`/`CostSource` | Example |
|---|---|---|
| **Analyzed** company (the one being studied) | `CompanyId` (owner) | Apple |
| **Counterparty** that fell into the analysis | `RelatedCompanyId` (nullable) | TSMC |

Relationship is **1:N**: Apple (`CompanyId`) owns many `RevenueSource` and many `CostSource` rows; each row optionally names one counterparty via `RelatedCompanyId`. Reverse navs (`Company.RevenueFromDependents` / `CostFromDependents`) make Apple→TSMC-as-cost also surface on TSMC's page as revenue-from-a-dependent. **One row, both directions.**

The proof columns live on the source row itself, which already holds both companies.

---

## Human entry with referenced provenance

### UI

A split screen wired to an endpoint browser:

```
┌──────────────────────────────┬───────────────────────────────────┐
│  LEFT: empty cells (DB)      │  RIGHT: raw API response (JSON/text)│
│                              │                                     │
│  Value:        [        ]    │  { "revenue": { "segments": [       │
│  Percentage:   [        ]    │      { "name": "iPhone",            │
│  Name:         [        ]    │        "value": 200583000000 }, ... │
│  Counterparty: [        ]    │  filing text: "...TSMC manufactures │
│                              │   substantially all of the          │
│  [ Use as reference ]        │   company's chips..."               │
└──────────────────────────────┴───────────────────────────────────┘
```

- **Left** = cells corresponding 1:1 to DB columns (`Value`, `Percentage`, `Name`, `RelatedCompany`, classification).
- **Right** = the structured response from the selected endpoint — formatted so the user can scan docs/text easily.
- The user types/confirms a value on the left, then **selects the backing text on the right and clicks "Use as reference."** The user never writes free-text proof; they only point at the returned response.

### Fields are bound to the source row (pre-fill + create)

The left cells are **bound to a `RevenueSource`/`CostSource` row** — nothing is retyped elsewhere. The source row is the source of truth for both the values and their proof.

- If EDGAR/Finnhub already fetched the row, the cells **pre-fill** from it — the user confirms and references rather than typing.
- If the API missed something, the user **creates** a new row here: the save upserts the `RevenueSource`/`CostSource` together with its proof, in one write.

### Each row links to its filing

When the proof is selected from an **open filing document** (not the Company Facts JSON), the save upserts a `Filing` by accession number and stores its id on the source row's `FilingId` — see the `Filing` section below. Saving from Company Facts (no filing open) leaves `FilingId` null; the row still carries its quote.

### What gets stored — one proof per row

Since `Company → RevenueSource/CostSource/CompanyRisk` is 1:N, **one filing extraction already produces N source rows**. Each row therefore needs exactly one proof, and it lives on the row:

| Column | Type | Notes |
|---|---|---|
| `Reference` | string? | WHERE in the document — the SEC Item / note / subheading (e.g. "Item 7. Management's Discussion") |
| `Evidence` | string? | the exact verbatim substring, **frozen at save time** and findable by a literal search in the filing |
| `FilingId` | long? FK→Filings (`Restrict`) | the document both came from; null when the proof came from Company Facts or a web source |

Notes:
- Counterparty (TSMC) is not part of the proof — it's `RelatedCompanyId` on the same row.
- `Evidence` is a **frozen snapshot**, so re-fetching the endpoint later can't silently change the proof.
- Per-cell proof rows were dropped: on the AI path every cell of a row carried a byte-identical quote (the model only ever produces one quote per record), and a classification is an inference — there is nothing in the filing to quote for it.

### Consuming the data

Trusted data = live, approved rows: `DeletedAt IS NULL AND Status = 0` (Approved). The contribution
review queue (`ContributionsController`) is what gates user-entered rows, and each row's
`Reference` / `Evidence` / `Filing` is shown next to it so a Manager can verify before approving.

---

## The proof filing — `Filing`

The reference's frozen snapshot answers *what text backs this cell*; the `Filing` answers *which document it came from*, as a first-class row you can link to on the graph. Soft-delete like every entity.

| Column | Type | Notes |
|---|---|---|
| `Id` | long PK | |
| `CompanyId` | long FK→Companies | the filer |
| `AccessionNumber` | string, **unique** | EDGAR identity — globally unique, so one row per filing |
| `Form` | string? | 10-K, 10-Q, 8-K… |
| `FilingDate` | DateTime? | |
| `PrimaryDocUrl` | string? | ready link to the document |
| `DeletedAt` | DateTime? | soft-delete convention |

- **One filing per source row.** The link lives on `RevenueSource`/`CostSource`/`CompanyRisk.FilingId` (nullable FK, `Restrict` — a cited filing can't be hard-deleted from under a row).
- **Upsert by accession.** The accession number is the key — the same 10-K referenced from two sources resolves to one `Filing` row, so the graph shows a single shared node.
- **On the graph:** a source's filing is carried on its leaf node (listed in the click popup) rather than drawn as its own node.

> **Filings are not events.** EDGAR filings used to be ingested into the `Event` table on refresh (`10-K`/`10-Q` → `EARNINGS`, `8-K` → `CORPORATE_ACTION`). That mapping was **removed**: a refresh maps only revenue/cost rows and creates no filing rows. A `Filing` exists only when a user references a filing document in phase 1.

---

## Flow

```
HUMAN ENTRY
  pick endpoint ─► response renders on right (Company Facts JSON or a filing doc)
  source row pre-fills cells (if auto-fetched) OR user creates it
  confirm/type the values (left) + select the backing passage (right) ─► USE SELECTION
  SAVE ROW
     ├─► if a filing doc is open: upsert Filing by AccessionNumber ─► FilingId
     └─► UPSERT RevenueSource/CostSource/CompanyRisk
                { values…, Reference, Evidence, FilingId }
         (one row, one proof — an omitted Reference/Evidence/FilingId keeps
          whatever citation the row already carries)

CONSUME
  graph/exports read live source rows (DeletedAt IS NULL, Status = Approved)
```

---

## Implementation note

Reuse the existing `StockService` / `IStockApiClient` shape — no new layers (project convention). Data access stays in the existing revenue/cost/risk repositories; the proof rides along on the row, so no extra repository is involved. Every write goes through `IContributionWriter.UpsertRow`, which applies the reviewer gate and the "don't clobber the citation when a write omits it" rule.

---

## Open gaps worth tracking

These are known holes, not blockers — listed so they're decided deliberately, not by accident.

1. **Stale proof.** Editing a row's values doesn't invalidate its `Evidence` — the quote can end up next to a number nobody re-checked against it. There is no staleness flag (the old per-field `ReferencedValue` column went with the review table).
2. **Derived values.** If `Percentage` is computed from two JSON numbers, that figure isn't literally *in* the `Evidence` quote. The quote cites the passage the row came from, not each arithmetic step.
3. **Unproved rows exist.** `Reference`/`Evidence` are nullable — a row can be saved with no citation at all, and the UI simply shows "no proof on record".
4. **Machine-sourced data (EDGAR/Finnhub).** Those rows have provenance (the XBRL fact / API field) but no quote, so their proof cells stay empty.
5. **Name resolution.** Evidence says "TSMC"; the DB Company is "Taiwan Semiconductor". Verifying a counterparty by hand still needs the alias/Wikidata resolver from `api-model.md`.
6. **No proof history.** Re-saving overwrites `Reference`/`Evidence` in place; nothing keeps the prior citation.
