# Master Architecture — SEC Filing Intelligence Pipeline

Single source of truth for the system that extracts structured intelligence from SEC filings and feeds it into the sector dependency graph behind the Macro Event Impact Modeller.

The system produces four record types — `RevenueSource`, `CostSource`, `RiskFactor`, `CompetitorEdge` — from **one shared extraction engine**. The four pipelines differ only in three things: which filing sections they target, what record they emit, and (for two of them) which extractor is primary.

**Governing rule:** XBRL is the calculator. The LLM is the locator and the prose reader. Always reconcile.

Companion documents:
- `sec-revenue-extraction-pipeline.md` — the detailed spec of the shared engine + Pipeline 1.
- `competitor-discovery-streaming-example.md` — a fully worked example of Pipeline 4 on the real streaming set.

---

## 1. System map

```
                         ┌──────────────────────────┐
                         │  SHARED EXTRACTION ENGINE │
   filing (CIK) ───────▶ │  acquire · structure ·   │
                         │  route · reconcile · emit │
                         └────────────┬─────────────┘
                                      │
                         ┌────────────▼─────────────┐
                         │     ARCHETYPE ROUTER      │
                         │  classify filer → A–F     │
                         │  dispatch to adapter      │
                         └────────────┬─────────────┘
                                      │
        ┌──────────────┬─────────────┼─────────────┬──────────────┐
        ▼              ▼             ▼             ▼              
  ┌───────────┐  ┌───────────┐ ┌───────────┐ ┌─────────────┐
  │  Revenue  │  │   Cost    │ │   Risks   │ │ Competitors │
  │  source   │  │  source   │ │           │ │             │
  │→RevenueSrc│  │→CostSource│ │→RiskFactor│ │→CompetEdge  │
  └─────┬─────┘  └─────┬─────┘ └─────┬─────┘ └──────┬──────┘
        │              │             │              │
        └──────────────┴──────┬──────┴──────────────┘
                              ▼
                   ┌─────────────────────┐
                   │ Sector dependency    │
                   │ graph (the modeller) │
                   └─────────────────────┘
```

Build the engine and router once; each pipeline is then a section-target config + an output schema (+ a swapped primary extractor where noted).

---

## 2. The shared extraction engine (five stages)

### Stage 0 — Acquire & structure
Pull three artifacts before any agent runs:
1. **Filing HTML** — for narrative Items (1 Business, 1A Risk Factors, 7 MD&A).
2. **`FilingSummary.xml`** — EDGAR's index of financial-statement R-files; every statement and note arrives pre-segmented and named, so Item 8 needs no header-finding.
3. **XBRL instance** — the tagged facts and their dimensional contexts.

### Stage 1 — Route sections
- **Items** are a static prior per pipeline (see each pipeline's "targets"). No agent needed to pick them.
- **Notes** are matched by name against `FilingSummary.xml` short-names (regex, not an LLM call): e.g. `segment`, `disaggregat`, `revenue`, `contingenc`, `commitment`.
- **Narrative sections** (Items 1, 1A, 7) are sectionized and scored by a cheap classifier (embeddings vs a per-pipeline lexicon) → keep top-k.

Output: ~3–8 candidate sections, each tagged with *why* it was selected. This pruning is what keeps LLM spend and EDGAR rate-limit pressure low.

**Per-pipeline TOC map.** Each pipeline routes a fixed set of SEC Items to its Flash agents and a fixed set of XBRL concepts to its Pro agent. This is the static prior referenced above (in code: `FilingSections.ItemsFor`).

| Pipeline | Narrative Items (→ Flash agents) | Item 8 notes | XBRL concepts (→ Pro agent) |
|---|---|---|---|
| **Revenue** | Item 7 | Segment (ASC 280), Disaggregation (ASC 606) | `RevenueFromContractWithCustomerExcludingAssessedTax` (flat + segment-axis member) |
| **Cost** | Items 1 (suppliers/raw materials), 7 | Cost-of-revenue line, segment-profit measure, content-amortization note, Commitments & Contingencies (purchase obligations), Concentrations (single-source %) + material-contract exhibits (Ex-10.x) | `CostOfRevenue`, `CostOfGoodsAndServicesSold`, `CostsAndExpenses`, `SellingGeneralAndAdministrativeExpense`; segment cost = revenue − `OperatingIncomeLoss` (segment-axis) |
| **Risks** | Items 1A, 7A, 3 | Commitments & Contingencies | — (prose only; no XBRL feed) |
| **Competitors** | Item 1 (Competition) | segment co-membership across filers | segment-axis members (membership index), DEF 14A peer group (external) |

### Stage 2 — Structured XBRL feed
Pull the pipeline's tagged concepts. Two reach levels: **company-total** facts come from the dimensionless `companyfacts` API (already plumbed in `StockApiClient.GetCompanyFacts`); **segment-level** facts (a concept scoped to a `StatementBusinessSegmentsAxis` member) are *not* in `companyfacts` and require parsing the filing's XBRL **instance document**, where each fact carries a `contextRef` resolving to its segment member. Deterministic; parallelize freely. For Risks this stage is empty (prose only).

### Stage 3 — Parallel LLM agents + Pro synthesizer
A two-tier agent design, not a single extractor:

1. **Parallel Flash agents** (in code: `FilingExtractionService.ScanChunksAsync`, bounded at `MaxParallel`) — one worker per routed Item-section, each returning prose candidates with verbatim proof. The map phase.
2. **Main Pro agent** (in code: `ExtractionChatService`, the "lead analyst") — its grounding is the **Flash digest *plus* the Stage-2 XBRL facts**. It fuses the two, **prefers the tagged number for monetary fields**, fills the rest from prose, and structures the final record(s). The reduce phase.

The XBRL feed into the Pro agent is what makes this a synthesizer rather than a fallback ladder: the agent always sees the authoritative numbers next to the prose, instead of XBRL and LLM competing as separate paths. For Risks the Pro agent runs on prose alone (no XBRL to fuse).

### Stage 4 — Reconcile & score
Runs **in code, after** the Pro agent structures the record — the numeric checks an LLM can't be trusted with. Does the value match a tagged XBRL fact within tolerance? Do disaggregated lines sum to the total? Is `segmentRevenue − segmentProfit` in range? Is the period a stub (predecessor/successor)? Agreement → high confidence; disagreement → flag, never silently pick. (Split of duty: the Pro agent *fuses and fills*; this stage *verifies the arithmetic*.) For the Risks pipeline this stage's job changes to **year-over-year novelty detection** and **node linkage** (§6).

### Stage 5 — Emit
Write the reconciled record with full provenance (Item / note / R-file / accession / period).

---

## 3. The archetype router

### The problem
The same economic concept (e.g. "streaming revenue") is reported under different segment names, different TOC locations, and different XBRL dimensions depending on the filer — and the same company renames and reorganizes it between filings. So extraction is **classify-then-dispatch**, not one extractor.

### The six archetypes

| Archetype | Example filers | Where the figure lives | Extraction |
|---|---|---|---|
| **A — Pure-play, single segment** | Netflix | ≈ total net revenue; geo-disaggregated | Income-statement total + ASC 606 geo note |
| **B — Named segment** | WBD (`Streaming`), Paramount (`Direct-to-Consumer`), Roku (`Platform`) | ASC 280 segment footnote | Parse segment note, filter segment-axis member |
| **C — Sub-line inside a segment** | Disney (`Direct-to-Consumer` in `Entertainment`; ESPN DTC in `Sports`) | Intra-segment revenue-category table | Parse segment-detail breakdown, filter category |
| **D — Disclosed product metric** | Comcast (Peacock in NBCU `Media`) | MD&A text / trending schedules | Targeted text/table mining |
| **E — Not disclosed** | Apple (TV+ in `Services`), Amazon (Prime Video in `Subscription services`) | Nowhere in the filing | Flag → external estimate / inference |
| **F — Foreign private issuer** | Spotify (20-F, IFRS; `Premium` / `Ad-Supported`) | 20-F "Operating & Financial Review" | Separate 20-F/IFRS adapter |

Dispatch is colour-coded by *how the number comes out*: A/B/C structured XBRL, D text/LLM, E not disclosed, F foreign taxonomy.

### Registry & profile

```csharp
public enum ReportingArchetype
{
    PurePlaySingleSegment, NamedStreamingSegment, SubLineWithinSegment,
    DisclosedProductMetric, NotDisclosed, ForeignPrivateIssuer
}

public sealed record FilerProfile
{
    public required string Cik { get; init; }
    public required string Name { get; init; }
    public required ReportingArchetype Archetype { get; init; }
    public IReadOnlyList<string> SegmentMemberLabels { get; init; } = []; // newest first; aliases capture renames
    public string? ParentSegment { get; init; }     // archetype C
    public string? ProductLabel { get; init; }      // archetype D
    public string FilingForm { get; init; } = "10-K";
    public int FiscalYearEndMonth { get; init; } = 12;
}
```

Seed registry (streaming) — CIKs reconciled against SEC `company_tickers.json` at build time:

| Filer | CIK | Archetype | Streaming label(s) | Notes |
|---|---|---|---|---|
| Netflix | 0001065280 | A | (total revenue) | Geo-only disaggregation; membership metrics dropped 2025 |
| Warner Bros. Discovery | 0001437107 | B | `Streaming` ← `DTC` | Renamed Q1 2025; corporate split + Paramount acquisition in progress |
| Disney | 0001744489 | C | `Direct-to-Consumer` in `Entertainment` | ESPN DTC under `Sports`; FY ends late Sept |
| Paramount Skydance | 0002041610 | B | `Direct-to-Consumer` | Skydance merger → stub periods; 3-segment recast |
| Comcast | 0001166691 | D | `Peacock` in `Media` | Not a GAAP segment; cable-net spinoff underway |
| Roku | 0001428439 | B | `Platform` | `Platform` vs `Devices` |
| Spotify | 0001639920 | F | `Premium` / `Ad-Supported` | Form 20-F, IFRS |
| Apple | 0000320193 | E | — | TV+ inside `Services` |
| Amazon | 0001018724 | E | — | Prime Video inside `Subscription services` |

### Adapters (per archetype)

Uniform interface; the logic inside differs.

```csharp
public interface IRevenueAdapter
{
    ReportingArchetype Handles { get; }
    Task<Extraction> ExtractAsync(FilingContext ctx, FilerProfile p);
}
```

- **A** — pull flat `RevenueFromContractWithCustomerExcludingAssessedTax`, no segment dimension.
- **B** — resolve the *current* segment member from the footnote (alias list handles renames), pull revenue scoped to that dimension, pull segment profit.
- **C** — two-step: resolve parent reportable segment, then the streaming category inside its disaggregation (`srt:ProductOrServiceAxis` member). Warn on splits (e.g. ESPN DTC under Sports).
- **D** — no tagged fact → LLM over the full MD&A/trending section + labels; confidence capped `Probable`.
- **E** — return `NotDisclosed` immediately → external estimator.
- **F** — 20-F/IFRS adapter; different taxonomy, "Operating & Financial Review" instead of MD&A.

### Cross-cutting normalizers (wrap every adapter)
- **Fiscal-year misalignment** — align to calendar quarters (Disney closes late Sept; peers Dec 31).
- **Predecessor/successor stub periods** — mid-year M&A splits a fiscal year into non-summing periods; detect multiple period contexts on one concept.
- **Recast prior periods** — reorganizations republish priors, often *furnished* in an 8-K (Item 7.01), not *filed*; prefer recast for time series, tag liability status.
- **Entity continuity** — resolve by `CIK` + `LEI` so a successor entity keeps its history through splits/spins/acquisitions.
- **8-K timeliness** — earnings-release Exhibit 99.1 carries segment tables weeks early; prefer for recency, reconcile to the 10-K.

### Fallback ladder (unknown filers)
1. Registry hit → encoded archetype + labels (highest confidence).
2. Auto-classify → scan segment footnote for lexicon-matching members → tentative B/C.
3. Single-segment + topic-heavy business description → A.
4. Product name only in MD&A prose → D, lower confidence.
5. Nothing → E, emit `NotDisclosed`, hand to external estimation.

### Confidence model
```csharp
public enum ConfidenceTier { Confirmed, Probable, Inferred }
```
- **Confirmed** — tagged XBRL fact passing reconciliation (sum + period + cross-read).
- **Probable** — prose/curated source, or a structured value failing one check.
- **Inferred** — externally estimated or derived from an input-output prior.

A numeric `ConfidenceScore` (0–1) accompanies the tier so downstream shock propagation can decay weak edges.

---

## 4. Pipeline 1 — Revenue source

| Aspect | Detail |
|---|---|
| Targets | Item 7 revenue tables; Item 8 Segment note (ASC 280) + Disaggregation note (ASC 606) |
| Mode | Structured-first (XBRL), LLM fallback |
| Engine reuse | **Direct** — this *is* the canonical use of the router |
| Emits | `RevenueSource` |
| Defining challenge | Segment renames; stub/recast periods |

```csharp
public sealed record RevenueSource
{
    public required CompanyEntity Company { get; init; }
    public required string IndustryKey { get; init; }     // canonical line of business
    public string? RawSegmentLabel { get; init; }
    public ReportingArchetype Archetype { get; init; }
    public decimal? RevenueUsd { get; init; }
    public decimal? SegmentProfitUsd { get; init; }       // segment EBITDA / operating income
    public double?  RevenueSharePct { get; init; }        // segment / total — materiality
    public string?  GeographyOrType { get; init; }        // disaggregation dimension
    public required Provenance Provenance { get; init; }
}
```

See `sec-revenue-extraction-pipeline.md` for the full adapter code.

---

## 5. Pipeline 2 — Cost source

| Aspect | Detail |
|---|---|
| Targets | Item 8 cost-of-revenue + segment profit; Item 7 cost MD&A; Item 1A/7A input & commodity exposure; commitments / content-amortization notes |
| Mode | Structured for cost-of-revenue & segment cost; LLM for input/commodity prose |
| Engine reuse | **Partial** — the segment-profit side |
| Emits | `CostSource` |
| Defining challenge | **GAAP reports cost by *function*, not by *input*** |

This is the hardest pipeline, and the reason is structural. The segment note hands you segment revenue *and* segment profit, so segment cost falls out by subtraction (`cost = revenue − segment profit`) — but that is cost by *function*, lumped together, not cost by *input commodity*, which is what a cost-push macro model needs. So this pipeline cannot live in the filing alone:

- **Functional cost** (cost of revenue, content amortization, SG&A) → from Item 8 + the segment note, structured.
- **Input attribution** (which commodity / supplier drives the cost) → fused from the supply-chain `SupplierFinding` edges + Leontief input coefficients. This is *derived* or *inferred*, never directly reported.
- **Input-side prose** (single-source warnings, commodity price sensitivity) → Item 1A (dependencies) and Item 7A (Quantitative & Qualitative Disclosures About Market Risk), LLM-extracted.

For media/streaming specifically the dominant cost line is **content amortization** plus content obligations, which sit in a dedicated note — so the streaming `CostSource` extractor targets that note the way the revenue extractor targets the segment note.

```csharp
public sealed record CostSource
{
    public required CompanyEntity Company { get; init; }
    public required string IndustryKey { get; init; }
    public CostNature Nature { get; init; }
    public string?  InputCommodity { get; init; }          // resolved input — often inferred
    public decimal? CostUsd { get; init; }
    public double?  CostShareOfRevenuePct { get; init; }
    public bool     IsSingleSourceInput { get; init; }      // from Item 1A
    public string?  CommodityExposureNote { get; init; }    // from Item 7A
    public CostAttribution Attribution { get; init; }
    public required Provenance Provenance { get; init; }
}

public enum CostNature { CostOfRevenue, ContentAmortization, Sga, CommodityInput, Logistics, Other }
public enum CostAttribution { ReportedDirect, DerivedFromSegment, InferredFromInputOutput }
```

The `Attribution` field is essential: a cost figure that was *reported* is trustworthy; one *derived* from segment subtraction is solid but functional; one *inferred from input-output* is a modelling estimate and must decay accordingly in shock propagation.

---

## 6. Pipeline 3 — Risks

| Aspect | Detail |
|---|---|
| Targets | Item 1A Risk Factors; Item 3 Legal Proceedings; Item 7A Market Risk; contingencies note |
| Mode | **LLM-primary** (prose), classify + dedup vs prior year |
| Engine reuse | No — text-driven, inverts the engine's polarity |
| Emits | `RiskFactor` |
| Defining challenge | Boilerplate dedup; severity rarely quantified; mapping risk → affected node |

This pipeline **inverts the engine's polarity**. In Revenue/Cost the LLM is the fallback and XBRL is primary; here Item 1A is unstructured prose, so the LLM *is* the primary extractor and there is no XBRL to reconcile against. Stage 4's job therefore changes:

- **Novelty detection** — diff this year's risk language against the prior filing; flag what is *new* versus recycled boilerplate (`IsNewThisYear`). New risk language is the high-signal part.
- **Node linkage** — link each risk to the `RevenueSource` / `CostSource` node it threatens, so a risk is an *edge into the graph*, not a free-floating sentence. A supply risk attaches to the relevant `CostSource`; a demand risk to a `RevenueSource`.
- **Severity** — rarely quantified in filings, so usually inferred; store as nullable and tag the inference.

```csharp
public sealed record RiskFactor
{
    public required CompanyEntity Company { get; init; }
    public RiskCategory Category { get; init; }
    public required string Summary { get; init; }          // paraphrased, never verbatim
    public RiskSeverity? Severity { get; init; }            // often inferred
    public bool IsNewThisYear { get; init; }                // YoY novelty
    public IReadOnlyList<string> AffectedNodeIds { get; init; } = []; // edges into the graph
    public string? SourceItem { get; init; }                // "1A" | "3" | "7A"
    public required Provenance Provenance { get; init; }
}

public enum RiskCategory { Operational, Financial, Regulatory, Supply, Geopolitical, Technology, Legal, Macro }
public enum RiskSeverity { Low, Medium, High, Critical }
```

---

## 7. Pipeline 4 — Competitors

| Aspect | Detail |
|---|---|
| Targets | Item 1 Competition; DEF 14A peer group; cross-filer segment co-membership + supply overlap |
| Mode | Hybrid: structured co-membership + LLM text |
| Engine reuse | **Inverted** — the router builds a membership index |
| Emits | `CompetitorEdge` |
| Defining challenge | Archetype-E blind spot; directional text; vertical exclusion |

This pipeline runs the router **backwards**. Every other pipeline is "one CIK in, its data out." Competitors fans the *classifier* across a filer universe to build a reusable **industry-membership index** (Phase A), then ranks a target against it (Phase B).

**Phase A — membership index.** Invert the classifier: find every filer whose segment/disaggregation notes expose a member matching the industry lexicon, gated by a materiality floor. Then **snowball** the archetype-E players (Apple, Amazon) that report no segment — they enter only because confirmed members name them in Competition text and peer groups.

**Phase B — rank.** Score each co-member on five independent signal families and combine:

```
seg 0.30 | cust 0.28 | supp 0.14 | peer 0.16 | text 0.12
```

```csharp
public sealed record CompetitorSignals
{
    public double SegmentOverlap   { get; init; }   // shared IndustryKey, revenue-weighted
    public double CustomerOverlap  { get; init; }   // Jaccard over shared >10% customers
    public double SupplierOverlap  { get; init; }   // Jaccard over shared upstream suppliers
    public double PeerGroupMention { get; init; }   // DEF 14A comp peers (1 mutual, 0.6 one-way)
    public double CompetitionText  { get; init; }   // Item 1 Competition naming (directional)
}

public sealed record CompetitorEdge
{
    public required CompanyEntity A { get; init; }
    public required CompanyEntity B { get; init; }
    public required string IndustryKey { get; init; }
    public required double Score { get; init; }
    public required CompetitorSignals Signals { get; init; }
    public required int CorroboratingSignals { get; init; }   // confidence from independence, not magnitude
    public required Provenance Provenance { get; init; }
}
```

Three guards that the worked example demonstrates:
- **Archetype-E rescue** — segment co-membership can't see Apple/Amazon; supplier-overlap (content/talent) + competition-text rescue them.
- **Directional text** — Netflix names no rivals; the signal fires from WBD/Disney/Paramount naming Netflix. Aggregate both directions.
- **Vertical exclusion** — a buyer/seller pair (e.g. Netflix distributed on Roku) is flagged vertical, not counted as a clean head-to-head competitor.
- **Industry-key granularity** — Spotify (audio) is a different `IndustryKey` than Netflix (video); the canonical key, not the lexicon match, is the membership decision.

See `competitor-discovery-streaming-example.md` for the full worked ranking.

---

## 8. Shared output schema

Every emitted record carries the same provenance base, so the graph layer treats all four uniformly.

```csharp
public sealed record CompanyEntity
{
    public required string CanonicalName { get; init; }
    public string? Lei { get; init; }
    public string? Cik { get; init; }
    public string? Ticker { get; init; }
    public string? CountryIso2 { get; init; }
    public string? GicsSubIndustry { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = [];
    public string? ParentLei { get; init; }
}

public sealed record Provenance
{
    public required SourceKind Source { get; init; }
    public required ConfidenceTier Confidence { get; init; }
    public required double ConfidenceScore { get; init; }   // 0..1
    public string? Evidence { get; init; }                   // accession / R-file / URL — the pointer
    public string? Reference { get; init; }                  // verbatim filing passage the record was pulled from — the traceable source text (per-record, vs SourceFieldReview's per-field proof)
    public required string CollectedByAgent { get; init; }
    public required DateTimeOffset CollectedAt { get; init; }
    public DateOnly? PeriodFrom { get; init; }
    public DateOnly? PeriodTo { get; init; }
    public bool IsStubPeriod { get; init; }                  // predecessor/successor
    public bool IsRecast { get; init; }                      // furnished recast vs filed
}

public enum SourceKind
{
    SecEdgar, FormSd, Def14a, ImportYeti, ImportGenius, Panjiva,
    CommercialDb, CommodityRegistry, IoTable, News
}
```

---

## 9. Composition into the graph

The four record types become graph elements:

- `RevenueSource` and `CostSource` → **node attributes** on a company, decomposed by line of business / input. Together they give per-segment margin.
- `CompetitorEdge` → **horizontal edges** between companies in the same `IndustryKey`.
- `RiskFactor` → **annotated edges** from a hazard to the `RevenueSource` / `CostSource` it threatens.

A macro event enters at a material/commodity node and propagates: down supply edges into `CostSource` (input shock), across `CompetitorEdge` (who else is exposed), and along `RiskFactor` links (which disclosed hazards amplify it). Each hop multiplies by edge weight × `ConfidenceScore`, so a `Confirmed` single-source dependency amplifies while an `Inferred` input-output estimate stays a diffuse background signal.

---

## 10. Operational notes

- **Idempotent caching** keyed by `(accession, sectionId, pipeline)` so re-runs don't re-spend agent calls; the membership index is cached and rebuilt only on new filings.
- **Bounded concurrency** on per-section fan-out. EDGAR requests a declared `User-Agent` and throttles ~10 req/s; Stage 1 pruning keeps you under it.
- **Provenance is mandatory** on every record. A trading pipeline cannot accept an unattributed number.
- **Entity continuity** through M&A is resolved centrally (CIK + LEI), so all four pipelines see consistent entities through splits, spins, and acquisitions.

---

## 11. Build order

1. **Shared engine** (Stages 0–1, 4–5) + **archetype router** (registry, adapters A–F, normalizers, fallback ladder, confidence model). Everything else depends on this.
2. **Pipeline 1 — Revenue source.** The canonical router use; validates the engine end to end.
3. **Pipeline 4 — Competitors.** Reuses the router inverted; needs only the membership index + ranker on top of the engine + the supply-chain edges.
4. **Pipeline 2 — Cost source.** Adds the supply-chain + Leontief fusion step for input attribution.
5. **Pipeline 3 — Risks.** Swaps in the LLM-primary extractor + YoY novelty + node linkage.

---

## Appendix — assumptions to verify at build time

- Registry CIKs are seed values; reconcile against SEC `company_tickers.json`.
- Segment labels change between filings; always resolve the live label from the segment footnote per period rather than trusting the registry blindly.
- Stub-period and recast handling must be live before any time series is trusted, because the current media set (Paramount–WBD, WBD split, Comcast spinoff) is mid-reorganization.
