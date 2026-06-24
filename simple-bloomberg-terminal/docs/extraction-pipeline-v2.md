# Extraction Pipeline v2

How one SEC filing becomes reviewable `RevenueSource` / `CostSource` / risk rows. **COST is the
reference node** — it has the full synthesizer setup (XBRL feed, per-record citation, reconcile).
This doc records how COST is wired and how to bring **REVENUE** and **RISK** up to the same shape.

> One-line model: **regex finds structure → a cheap LLM triages it → parallel Flash workers read the
> chosen chunks → one Pro agent fuses their prose with tagged XBRL → code verifies the arithmetic.**

---

## 1. The shared engine (all three nodes)

The pipeline is node-agnostic; each node only swaps prompts, the routed Items, and the saved entity.

```
FetchRawAsync ──▶ BuildHeadings ──▶ TriageHeadingsAsync ──▶ PackHeadings ──▶ ScanChunksAsync ──▶ FormatDigest
  (sec2md→md,     (regex: Item       (LLM picks the       (consecutive       (≤6 parallel Flash    (cached digest,
   raw HTML        boundaries +        worthwhile           same-Item           workers, one per      key per node)
   fallback)       sub-headings)       headings; all        headings packed     chunk; Running/Done         │
                                       Item 7 forced)       ≤MaxChunkChars)     events → widget)            ▼
                                                                                              GroundingAsync (Pro agent)
Item 8 ──▶ BuildSection (regex slice → paragraph-packed chunks; NO triage — tables stay whole)        │
                                                                                              + XBRL feed (COST only)
                                                                                                      ▼
                                                                                       ExtractionChatService
                                                                                       (lead-analyst Pro agent)
                                                                                       ```save``` block → entity row
```

Stages (all in `Services/FilingExtractionService.cs` unless noted):

| Stage | What | LLM? |
|---|---|---|
| `FetchRawAsync` | filing → clean markdown (sec2md sidecar), raw SEC HTML fallback; cached per filing | no |
| `BuildHeadings` (`FilingSections.cs`) | regex `^…Item\s+(\d+[A-Z]?)` finds Item boundaries, slices target Items, splits into sub-headings | no |
| `TriageHeadingsAsync` | feeds heading **titles only** (cheap) → model returns the ids worth reading; falls back to all on failure; **Item 7 always force-added** | **yes** (1 call) |
| `PackHeadings` | packs consecutive same-Item headings into one worker call ≤ `FilingSections.MaxChunkChars` (4000) — small titles ride together, fewer calls | no |
| `BuildSection` (`FilingSections.cs`) | Item 8 only: sequential paragraph chunks of the whole section (tables detached from headings) — no triage | no |
| `ScanChunksAsync` → `ScanChunkAsync` | up to `MaxParallel=6` Flash workers; each reads one chunk → `{sources:[…]}` with verbatim `proof`; dedupe by name | **yes** (per chunk) |
| `FormatDigest` | compact text digest of all candidates → cached at `FindingsKey(accession,doc,node)` | no |
| `GroundingAsync` (`ExtractionChatService.cs`) | digest (+ XBRL for COST) becomes the Pro agent's grounding | no |
| `StreamReplyAsync` (`ExtractionChatService.cs`) | the lead-analyst Pro agent; emits ```save``` blocks the widget persists | **yes** (chat) |

**Live progress.** `ScanAutoAsync` takes `Action<ScanProgress>? onProgress`, emitting `Planned`
(the full chunk plan) then `Running`/`Done`/`Error` per chunk. `ExtractionController.ApplyScanProgress`
folds these into `ScanJob.Sections` (Item → chunk states) under a lock; the widget renders one
expandable box per Item with live per-call status. See `docs/ai-popup-chat.md`.

---

## 2. The node seams (where a node differs)

A node is defined entirely by these switch points — nothing else in the engine cares which node runs:

| Seam | File | COST today |
|---|---|---|
| Routed SEC Items | `FilingSections.ItemsFor` | `["1","7","8"]` |
| Flash worker prompt | `FilingExtractionService.SystemFor` | cost labels/segments/suppliers + proof; **de-emphasises transcribing totals** |
| Pro agent prompt + ```save``` schema | `ExtractionChatService.SystemFor` | fuses prose + XBRL; prefers tagged `value`; emits `reference` |
| XBRL feed | `ExtractionChatService.GroundingAsync` | **COST-only** — see §3 |
| Classification enum | `Models/Enums` (`CostBase`) | `COGS / OPEX / TOTAL_COSTS` |
| Save UI options | `wwwroot/js/site.js` `CLASS_OPTS` | mirrors `CostBase` |
| Saved entity | `Models/Entities/CostSource.cs` | has `Reference` (per-record citation) |

All three nodes return the same `{"sources":[…]}` envelope so `Parse` is shared. The save-block JSON
must match `site.js` `normalizeSave()`.

---

## 3. How COST is set up (the reference implementation)

COST adds a **third feed** to the two-tier Flash→Pro design: **structured XBRL** — the audited tagged
numbers. The rule: *XBRL is the calculator, the LLM is the reader.* The dollar figure comes from a
tagged fact; the LLM finds and structures the label/segment/supplier.

### 3.1 Company-total cost — `Services/XbrlFacts.cs`
Pure picker over EDGAR `companyfacts`. `AnnualForEnd(facts, end, names)` returns the 10-K USD point
whose period **end matches the filing's report date** (not the newest on file), trying concept names
in order:
- `Cogs` = `CostOfRevenue` → `CostOfGoodsAndServicesSold` → `CostsAndExpenses`
- `Opex` = `OperatingExpenses` → `SellingGeneralAndAdministrativeExpense`

`companyfacts` is **dimensionless** → company-total only, no per-segment.

### 3.2 Per-segment cost — `Services/XbrlInstanceReader.cs`
The dimensional figures `companyfacts` can't carry. Discovers the filing's XBRL **instance document**,
joins each segment's tagged revenue and `OperatingIncomeLoss` by `contextRef`, and implies
`Cost = Revenue − OperatingIncome` per segment. Each `SegmentCost.Reconciles` flags `0 ≤ Cost ≤ Revenue`
(false ⇒ a wrong/non-GAAP profit measure was matched ⇒ unreliable). Best-effort: an instance miss
just leaves the company-total block.

### 3.3 Fusing it — `ExtractionChatService.GroundingAsync` / `BuildCostXbrlAsync`
For COST only, appends an `XBRL TAGGED FACTS` block (company-total + per-segment, with a
Σ-segment-revenue-vs-total **sum check**) to the prose digest. Cached per filing (`cost-xbrl:{accession}`),
one SEC round-trip. The Pro prompt instructs: **prefer the tagged figure for `value`**, use the
workers' prose for name/segment/supplier, **flag disagreements rather than silently choosing**.

### 3.4 Per-record citation — `Reference`
`CostSource.Reference` (migration `AddCostSourceReference`) holds the verbatim passage (SEC Item/note +
source text) the **whole record** was drawn from — distinct from the per-field `proof` in `Reviews`.
The ```save``` schema carries a `reference` field; `SaveBatchItem`/`normalizeSave` pass it through.

### 3.5 Reconcile (code-side)
Numeric checks the LLM can't be trusted with stay in code: tagged-fact tie, segment sum check,
subtraction sanity, same-period. Disagreement → store but **flag**, never silently pick.

---

## 4. Refactor REVENUE to match COST

Revenue is the easiest port — the XBRL plumbing already exists and `XbrlInstanceReader` already reads
**segment revenue** (it's half of the cost subtraction).

| Step | Change |
|---|---|
| 1. XBRL feed | In `GroundingAsync`, run an XBRL block for `REVENUE` too. Reuse `XbrlFacts.AnnualForEnd` with revenue concepts (`Revenues`, `RevenueFromContractWithCustomerExcludingAssessedTax`) for the company total; reuse `XbrlInstanceReader` segment **revenue** directly (no subtraction needed). Generalise `CostXbrlAsync` → a node-parameterised `XbrlAsync`, or add a sibling `RevenueXbrlAsync`. |
| 2. Pro prompt | Revenue `SystemFor` (the `_ =>` branch) gains the same "prefer the tagged figure for `value`, flag disagreements" language. |
| 3. Flash prompt | Re-scope the revenue worker (`FilingExtractionService.SystemFor` default branch) toward **labels/segments/customers + proof**, de-emphasising exact-total transcription (XBRL now owns totals) — mirror the COST wording. |
| 4. `Reference` | Add `string? Reference` to `Models/Entities/RevenueSource.cs` (+ migration); add `reference` to the revenue ```save``` schema and ensure `SaveBatch` persists it. |
| 5. Reconcile | Segment revenues sum to the company-total revenue fact (the Σ check `BuildCostXbrlAsync` already computes); flag mismatch. |

Revenue routes `["7","8"]` today — likely fine; only widen if a revenue disclosure consistently lives
elsewhere.

---

## 5. Refactor RISK to match

Risk has **no dollar figures**, so the XBRL feed and numeric reconcile **do not apply** — don't force
them. The COST pattern that *does* transfer is **per-record citation + prompt discipline**.

| Step | Change |
|---|---|
| 1. `Reference` | Add a per-record citation to the saved risk entity (the verbatim Item 1A/7A passage) + `reference` in the risk ```save``` schema. This is the main parity gain. |
| 2. Prompt discipline | Keep the existing "ground every claim; say so when absent" wording; ensure the risk Flash + Pro prompts match the COST structure (one record per disclosed risk, scope + note + proof). |
| 3. No XBRL / no reconcile | Explicitly N/A — risk's value-add is **coverage and dedup** of disclosed factors, not numeric reconciliation. Don't add an XBRL branch for `RISK` in `GroundingAsync`. |
| 4. Routing | Risk already routes `["1A","7A"]` (force-add of Item 7 in `ScanAutoAsync` is a no-op for risk) — leave as is. |

---

## 6. Refactor checklist (per node)

- [ ] `ItemsFor` routes the right Items.
- [ ] Flash `SystemFor` scoped to labels/segments/counterparties + verbatim proof (totals deferred to XBRL where numeric).
- [ ] Pro `SystemFor`: prefers tagged `value`, flags disagreements (numeric nodes), emits `reference`.
- [ ] `GroundingAsync` feeds XBRL for numeric nodes (COST, REVENUE); **not** RISK.
- [ ] Entity has `Reference`; migration added; ```save``` schema + `normalizeSave` + `SaveBatch` carry it.
- [ ] Code-side reconcile for numeric nodes; flag (never hide) mismatches.
- [ ] `CLASS_OPTS` in `site.js` mirrors the node's enum.

---

## File map
| File | Role |
|---|---|
| `Services/FilingExtractionService.cs` | the engine: fetch → triage → pack → parallel Flash; `SystemFor` (Flash prompts); progress callback |
| `Services/FilingSections.cs` | regex heading/section parsing; `ItemsFor`; chunk packing budget |
| `Services/ExtractionChatService.cs` | Pro agent: `SystemFor` (prompts + ```save``` schema), `GroundingAsync`, COST XBRL fusion |
| `Services/XbrlFacts.cs` | company-total tagged facts (dimensionless `companyfacts`) |
| `Services/XbrlInstanceReader.cs` | per-segment figures from the XBRL instance document |
| `Models/Entities/CostSource.cs` | `Reference` per-record citation (reference entity for the pattern) |
| `docs/cost-extraction.md` | the original COST design rationale (XBRL synthesizer) |
| `docs/ai-popup-chat.md` | the detached widget, section-tree progress, save flow |
</content>
</invoke>
