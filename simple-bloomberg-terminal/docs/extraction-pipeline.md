# Extraction Pipeline — human entry + AI review

A two-phase pipeline for gathering **cost & revenue source** data with provenance, then having an AI act as an analyst-reviewer that verifies each value against the source text it was drawn from. Each per-field proof links to the **`Filing`** it was drawn from — so a source can cite several filings (one per cell) and connects to all of them on the graph.

Scope: `RevenueSource` and `CostSource` first. Companion to `api-model.md` (where the data comes from), `external-api.md` (the macro side), and `web_search.md` (discovering counterparty companies via Perplexity when filings don't name them).

---

## The two companies on every row

A cost/revenue source is an edge between two companies — this is already in the model, no new columns:

| Role | Column on `RevenueSource`/`CostSource` | Example |
|---|---|---|
| **Analyzed** company (the one being studied) | `CompanyId` (owner) | Apple |
| **Counterparty** that fell into the analysis | `RelatedCompanyId` (nullable) | TSMC |

Relationship is **1:N**: Apple (`CompanyId`) owns many `RevenueSource` and many `CostSource` rows; each row optionally names one counterparty via `RelatedCompanyId`. Reverse navs (`Company.RevenueFromDependents` / `CostFromDependents`) make Apple→TSMC-as-cost also surface on TSMC's page as revenue-from-a-dependent. **One row, both directions.**

The review table below never re-stores these two companies — it points at the source row, which already holds both.

---

## Phase 1 — human entry with referenced provenance

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

The left cells are **bound to a `RevenueSource`/`CostSource` row via the FK** — they are not retyped into the review table. The source row is the source of truth for the values; `SourceFieldReview` only stores proof + verdict.

- If EDGAR/Finnhub already fetched the row, the cells **pre-fill** from it — the user confirms and references rather than typing.
- If the API missed something, the user **creates** a new row here: on save the `RevenueSource`/`CostSource` is inserted first, *then* the `SourceFieldReview` FK is set to it.

**The FK forces ordering: no reference without a row.** `RevenueSourceId`/`CostSourceId` can only point at a row that exists, so the source row is always created (or already present) before its references. Editing a pre-filled cell writes back to the **source row**, not the review row.

### Each proof links to its filing

When the proof is selected from an **open filing document** (not the Company Facts JSON), the same "Use as reference" action upserts a `Filing` by accession number and stores its id on **that cell's `SourceFieldReview` (`FilingId`)** — see the `Filing` section below. Because proof is per-field, different cells can cite different filings, so one source connects to *all* of them on the graph. Referencing from Company Facts (no filing open) sets that cell's `FilingId` to null.

### What gets stored — one row per cell (1:N)

Each "Use as reference" action writes one `SourceFieldReview` row capturing **which endpoint** and **which part of the response** backs that cell. Provenance is **per-field**, because different cells point at different sections of the response (the number from an XBRL fact, "TSMC" from a risk-factor paragraph, the label from a segment name).

So **one source row → many `SourceFieldReview` rows (1:N), keyed by `Field`** — same FK (`RevenueSourceId`) repeated, `Field` differs. The unit of a review is `(source row, field)`: one proof + one mark per cell. A unique constraint on `(RevenueSourceId, Field)` / `(CostSourceId, Field)` keeps it to one current reference per cell.

Critically, the reference stores a **frozen snapshot** of the selected text — not just a pointer — so re-fetching the endpoint later can't silently change the proof under the reviewer.

---

## Phase 2 — AI analyst-reviewer

After the user submits, an AI pass iterates **only rows that have both a filled cell and a reference**. For each, it judges: *does the referenced snapshot actually support this value?* and writes:

- `Mark` = **1** (pass) or **0** (fail)
- `Rationale` = one line on why (recovers the nuance a bare 0/1 loses)

`Mark IS NULL` = not yet reviewed. Re-running the pass overwrites the mark in place (single-table model — no review history kept, by choice).

### Consuming the data

Trusted data = the verified set:

```sql
-- revenue rows that passed review
SELECT rs.* FROM RevenueSources rs
JOIN SourceFieldReviews r
  ON r.RevenueSourceId = rs.Id AND r.Mark = 1
WHERE rs.DeletedAt IS NULL;
```

Render/graph only `Mark = 1` rows; treat `Mark = 0` and `Mark IS NULL` as untrusted.

---

## The table — `SourceFieldReview`

One table: per-field reference (phase 1) + nullable verdict (phase 2). Soft-delete like every entity.

| Column | Type | Notes |
|---|---|---|
| `Id` | long PK | |
| `CompanyId` | long FK→Companies | analyzed company (Apple); denormalized for "all reviews for X" |
| `Relation` | enum `RelationKind` {COST, REVENUE} | discriminator — which source table |
| `RevenueSourceId` | long? FK→RevenueSources | exactly one of the two set, |
| `CostSourceId` | long? FK→CostSources | matching `Relation` (check constraint) |
| `Field` | enum `ReviewableField` {VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION} | which cell this row proves |
| `Endpoint` | string | which API endpoint produced the proof |
| `ReferencePointer` | string | JSON path / text offset the user selected |
| `ReferenceSnapshot` | string | literal proof text, **frozen at reference time** |
| `ReferencedValue` | string? | value snapshot at reference time → staleness detection |
| `FilingId` | long? FK→Filings | the filing this cell's proof came from (null if from Company Facts). Per-field, so one source cites many filings via its reviews |
| `Mark` | int? | null = unreviewed, 0 = fail, 1 = pass |
| `Rationale` | string? | AI's reason for the mark |
| `ReviewedAt` | DateTime? | when phase 2 last ran on this row |
| `ReviewerModel` | string? | model id, for reproducibility |
| `DeletedAt` | DateTime? | soft-delete convention |

Notes:
- Counterparty (TSMC) is **not** a column here — it's `RelatedCompanyId` on the linked source row.
- `Field = RELATED_COMPANY` is how you review "is TSMC really Apple's cost source?" separately from reviewing the dollar amount.
- Two nullable FKs (not a bare polymorphic id) keep real DB referential integrity in EF Core.

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

- **Filing per proof, not per source.** The link lives on `SourceFieldReview.FilingId` (nullable FK, `Restrict`). Because proof is per-field, one source can cite several filings — `VALUE` from a 10-K, `RELATED_COMPANY` from an 8-K. A source's proof filings = the distinct filings across its reviews.
- **Upsert by accession.** The accession number is the key — the same 10-K referenced from two cells or two sources resolves to one `Filing` row, so the graph shows a single shared node.
- **On the graph:** each source emits an edge `rev:{id}`/`cost:{id}` ──proof──▶ `filing:{id}` for every distinct filing its reviews cite. The source connects to every filing it appears in.

> **Filings are not events.** EDGAR filings used to be ingested into the `Event` table on refresh (`10-K`/`10-Q` → `EARNINGS`, `8-K` → `CORPORATE_ACTION`). That mapping was **removed**: a refresh maps only revenue/cost rows and creates no filing rows. A `Filing` exists only when a user references a filing document in phase 1.

---

## Flow

```
PHASE 1 (human)
  pick endpoint ─► response renders on right (Company Facts JSON or a filing doc)
  source row pre-fills cells (if auto-fetched) OR user creates it
     └─► ensure RevenueSource/CostSource row exists (INSERT if new)
  per cell: confirm/type value (left) + select proof text (right) ─► "Use as reference"
     ├─► if a filing doc is open: upsert Filing by AccessionNumber ─► FilingId
     └─► UPSERT SourceFieldReview { CompanyId, Relation, source FK,
                                    Field, Endpoint, Pointer,
                                    Snapshot, ReferencedValue, FilingId, Mark=NULL }
         (one row per cell — same source FK, different Field; FilingId may differ per cell,
          so the source ──proof──▶ filing edges fan out to every filing it cites)

PHASE 2 (AI, on submit)
  for each SourceFieldReview where cell filled AND Snapshot present AND Mark IS NULL:
     ask Claude: does Snapshot support the value in <source>.<Field>?
     ─► UPDATE Mark = 0|1, Rationale, ReviewedAt, ReviewerModel

CONSUME
  graph/exports read source rows WHERE a SourceFieldReview with Mark=1 exists
```

---

## Implementation note

Reuse the existing `StockService` / `IStockApiClient` shape — no new layers (project convention). Data access stays in repositories; add a `ISourceFieldReviewRepository`. The phase-2 reviewer is a service that pulls unreviewed rows, calls Claude, and persists marks — same client/service/repo split as the EDGAR refresh.

---

## Open gaps worth tracking

These are known holes, not blockers — listed so they're decided deliberately, not by accident.

1. **Stale pass.** User edits the source value after a `Mark=1`. The mark now lies. Mitigation: compare current value to `ReferencedValue` on save; if changed, reset `Mark = NULL`. (Needs a hook on source-row update.)
2. **Derived values.** If the user computes `Percentage` from two JSON numbers, the value isn't literally *in* the snapshot. The reviewer prompt must permit arithmetic/inference or these always score 0.
3. **Unreferenced rows are invisible.** Phase 2 skips rows without a reference. A value with no proof is never reviewed yet may still exist — make sure the consumer (`Mark=1` filter) treats it as untrusted, not hidden.
4. **`Mark = 0` has no feedback loop yet.** A fail should route back to the user to fix/re-reference. Undefined here — UI concern for later.
5. **Machine-sourced data (EDGAR/Finnhub).** Those rows have provenance (the XBRL fact / API field) but no *user* reference. Option: auto-emit `SourceFieldReview` rows with `Endpoint=EDGAR`, `ReferenceSnapshot=<the fact>`, so machine data flows through the same review pass instead of being trusted blindly.
6. **Name resolution.** Snapshot says "TSMC"; DB Company is "Taiwan Semiconductor". `Field=RELATED_COMPANY` reviews need the alias/Wikidata resolver from `api-model.md` or legitimate matches fail.
7. **Binary mark is coarse.** 1/0 can't separate "right number, wrong company" from "completely wrong" — `Rationale` is the only place that nuance survives.
8. **No review history.** Single-table, in-place overwrite by design. If you later want an audit trail of re-reviews, this is where a separate verdict table would come back.
