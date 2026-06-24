# Cost Source Extraction (SEC Filings)

How to pull **accurate cost figures** out of a company's SEC filing and store them as `CostSource` rows. This replaces the current pure-LLM cost path in `FilingExtractionService` (the `ExtractionNode.COST` branch).

Scope: extraction only. Get the numbers right, attribute them to the filing, flag when they don't reconcile. No macro-model / input-commodity work here.

> **Rule:** XBRL is the calculator, the LLM is the reader. The dollar figure comes from a tagged XBRL fact wherever one exists; the LLM finds and structures the rest.

---

## 1. Why the current path is weak

The live extractor runs one Flash prompt per filing chunk and asks the model to read a cost line and return `{name, classification ∈ {COGS,OPEX,TOTAL_COSTS}, value, percentage, related_company}`.

Problems:

| Problem | Consequence |
|---|---|
| LLM reads the number | The authoritative tagged `CostOfRevenue` fact is ignored; the model paraphrases a figure that is already machine-tagged. |
| No XBRL reaches the Pro agent | `ExtractionChatService` (the main agent) grounds on the Flash digest only — it never sees the tagged facts, so it can't fill `value` from the authoritative number. |
| No reconciliation | Nothing checks the figure against the tagged fact or that parts sum to the total. |

---

## 2. The flow (synthesizer model)

Three feeds converge on **one main agent** that structures the final saved record. This is the existing two-tier agent design (`ScanChunksAsync` Flash workers → `ExtractionChatService` Pro agent) with **structured XBRL added as a third feed**.

```
  ┌─ TOC routing (§3) ─ pick the SEC Items that carry cost ─┐
  │                                                          │
  ▼                                                          ▼
PARALLEL FLASH AGENTS                          STRUCTURED XBRL READER
one per Item-section chunk                     GetCompanyFacts → tagged
(ScanChunksAsync, MaxParallel=6)               cost concepts (§4)
  │ prose cost candidates + verbatim proof       │ exact audited numbers
  └──────────────┬───────────────────────────────┘
                 ▼
          MAIN PRO AGENT  (ExtractionChatService, "lead analyst")
          grounding = Flash digest + XBRL facts
          fills missing fields, prefers the tagged number for `value`,
          structures one record per cost
                 │
                 ▼
          CODE-SIDE RECONCILE (§5) — numeric checks the LLM can't be trusted with
                 │
                 ▼
          ```save``` block → CostSource row
```

Key split: **the Pro agent fuses and fills; a code helper verifies the arithmetic.** Sum checks and "revenue − profit ≥ 0" are math — an LLM will fudge them, so they stay in code.

**Every record carries a `Reference`.** The Pro agent must put, per emitted cost record, the verbatim passage from the filing it pulled the figure/label from — the SEC Item or note plus the source text — so each row is traceable to where it came from. This is a *per-record* source passage, distinct from the existing *per-field* `proof` substrings (`SourceFieldReview`): `proof` backs one field, `Reference` is the record's overall citation.

---

## 3. TOC routing — which Items feed the cost agents

Cost and supplier disclosure is spread across more of the filing than revenue. `FilingSections.ItemsFor(COST)` must route `["1","7","8"]` (today it returns `["7","8"]` — add Item 1), plus a separate fetch for material-contract exhibits:

| SEC Item / note | What the Flash agents read it for |
|---|---|
| **Item 1 (Business)** | "Sources & Availability of Raw Materials" — **named principal suppliers, raw materials, single-source dependence**. The canonical supplier disclosure. |
| **Item 7 (MD&A)** | Narrative cost discussion — cost drivers, segment cost commentary, the Contractual-Obligations table (forward purchase obligations). |
| **Item 8 — income statement** | The cost-of-revenue line and the segment-profit measure (Segment note). |
| **Item 8 — content-amortization note** | Media/streaming's dominant cost. |
| **Item 8 — Commitments & Contingencies note** | Purchase obligations, take-or-pay, minimum supply commitments. |
| **Item 8 — Concentrations note** | "one vendor = X% of purchases", single-source supplier %. |
| **Material contract exhibits (Item 15 / Ex-10.x)** | Actual supply-agreement counterparties — a separate document fetch, not part of the 10-K body. |

Item 8 is scanned sequentially in document order (tables stay intact); Items 1 and 7 go through heading triage. Item 8's tagged figures now also come through XBRL (§4), not just the Flash reading.

> **Supplier identity defers to discovery, not the cost agents.** The cost pipeline reads suppliers only to *attribute cost*; resolving a supplier to a real entity (ticker/LEI) is `CounterpartyDiscoveryService`'s job, fed by `SourceKind.FormSd` (conflict minerals) and the trade-data sources (`ImportYeti`/`ImportGenius`/`Panjiva`) — don't re-derive identity in the cost path.

---

## 4. The XBRL feed

`StockApiClient.GetCompanyFacts` already returns tagged facts (`EdgarCompanyFacts` → `us-gaap` concepts → `EdgarFact{End,Val,Fy,Fp,Form}`). A thin reader pulls the cost concepts for the filing's period and hands them to the Pro agent's grounding:

- `us-gaap:CostOfRevenue`
- `us-gaap:CostOfGoodsAndServicesSold`
- `us-gaap:CostsAndExpenses`
- `us-gaap:SellingGeneralAndAdministrativeExpense`

(Pick whichever the filer actually tags — see §7.) These are **company-total** figures and need no new plumbing.

**One limit to know:** `companyfacts` is *dimensionless* — it has no segment axis, so **per-segment** cost is not in it. Company-total cost is fully covered here; segment cost (`segmentRevenue − segmentProfit` per segment) needs the filing's XBRL *instance document*, which is the only genuinely new plumbing and is deferred to Phase 2.

---

## 5. Reconcile (code-side)

After the Pro agent structures a record, a code helper checks it before save:

- **Tagged-fact tie** — does the saved total equal the tagged `CostOfRevenue` fact within tolerance?
- **Sum check** — do per-segment costs sum to the company total? (Phase 2.)
- **Subtraction sanity** — is `segmentRevenue − segmentProfit` between 0 and `segmentRevenue`? Negative ⇒ wrong profit measure matched. (Phase 2.)
- **Period** — same fiscal period across compared facts; watch predecessor/successor stub periods.

Agreement → store clean. Disagreement → store but flag (`DataSource` stays `EDGAR`, mismatch noted), never silently pick.

---

## 6. What changes in the code

| Piece | Change |
|---|---|
| `Services/XbrlFactReader.cs` | **new** — wrap `GetCompanyFacts`; `GetFact(concept, fy, fp)` for the cost concepts (§4). |
| `Services/ExtractionChatService.cs` `GroundingAsync` | **add the XBRL facts to the grounding** next to the Flash digest, so the Pro agent sees tagged numbers. This is the core change. |
| `Services/ExtractionChatService.cs` COST prompt | Tell the agent: prefer the tagged XBRL number for `value`; use Flash prose for `name`/segment/supplier; say so when they disagree. |
| `Services/FilingExtractionService.cs` COST prompt | Re-scope Flash workers toward labels/segments/suppliers + proof, leaning less on reading exact totals (XBRL now owns those). |
| `Models/Entities/CostSource.cs` | **add `string? Reference`** — the per-record source passage (§2). The save-block schema (`ExtractionChatService` COST prompt) gains a `reference` field the agent fills. One small migration. |
| Reconcile helper | small, applies §5 checks before save. |

The rest of the entity is unchanged: `CostBase` holds the functional class, `Value` the dollars, `Percentage` the share, `Name` the segment/line label, `DataSource = EDGAR` the provenance. The only schema change is the `Reference` column.

---

## 7. Build plan

**Phase 1 — XBRL into the Pro agent (company-total cost).**
1. `XbrlFactReader` over `GetCompanyFacts`.
2. Feed its facts into `GroundingAsync`; update the COST prompts (Pro + Flash).
3. Reconcile helper — tagged-fact tie + period check; flag mismatches.

**Phase 2 — Segment cost (only if per-segment is wanted).**
4. Instance-document fetch + dimensional-context parser (`contextRef` → segment member).
5. Segment subtraction + sum check.

---

## 8. Verify at build time

- Which exact `us-gaap` concept the target filers tag for cost — `CostOfRevenue` vs `CostOfGoodsAndServicesSold` vs `CostsAndExpenses`. Pick per filing from what's present.
- Whether the filers tag a clean per-segment profit measure in the instance doc (some report only a non-GAAP segment measure) — determines if Phase 2 is reliable for them.
- Cache the XBRL read by `(cik, fy)` and the instance-doc fetch by `(accession)`; stay under EDGAR's ~10 req/s.
