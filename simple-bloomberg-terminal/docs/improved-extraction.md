# Industry Revenue Extraction Pipeline (SEC Filings)

A classify-then-dispatch pipeline for extracting **industry- and segment-specific revenue** (and segment profit) for a public company from its SEC filings, emitted as a `RevenueSource` record with provenance and a confidence score.

This document describes the *target* design only. It is not a description of any existing implementation.

---

## 1. Purpose & scope

**Goal.** Given a company (resolved to a `CIK`), produce the revenue it earns from a specific line of business (e.g. streaming), as a structured, audited, period-correct figure with a confidence tier and a pointer back to the exact filing location it came from.

**Where it fits.** This is the component that populates the `RevenueSource` nodes of the wider macro/geopolitical impact model. It sits downstream of entity resolution and feeds the sector dependency graph.

**Governing principle.**

> XBRL is the calculator. The LLM is the locator and the prose reader. Always reconcile the two.

The single most important design decision is that the authoritative dollar figure comes from a machine-tagged XBRL fact wherever one exists. The LLM is used to *find* the right section and to read disclosures that are prose-only — never to "read a number off a page" when that number is already tagged.

---

## 2. The core problem

"Revenue from business line X" is one economic concept, but it is reported under many different structural patterns, and the same company **renames and reorganizes** it across filings. Examples from the streaming industry:

- Netflix reports streaming as essentially the whole company, disaggregated only by region.
- Warner Bros. Discovery reports a top-level `Streaming` segment — renamed from `Direct-to-Consumer` / `DTC` in Q1 2025.
- Disney reports `Direct-to-Consumer` as a *sub-line inside* the `Entertainment` segment (Disney+/Hulu), with ESPN streaming inside the `Sports` segment.
- Comcast does not report Peacock as a GAAP segment at all — it is a product metric inside the NBCUniversal `Media` segment.
- Spotify is a foreign private issuer: Form 20-F, IFRS, `Premium` vs `Ad-Supported`.

A single extractor cannot handle these. The pipeline therefore **classifies each filer into a reporting archetype, then dispatches to a matching adapter**, with a fallback ladder for unknown filers.

---

## 3. Reporting archetypes

The taxonomy below is worked through the streaming industry but is structurally reusable for any sector.

| Archetype | Example filers | Where the revenue lives | Extraction method |
|---|---|---|---|
| **A — Pure-play, single segment** | Netflix | Streaming ≈ total net revenue; geo-disaggregated | Income-statement total + ASC 606 geography note |
| **B — Named streaming segment** | WBD (`Streaming`), Paramount (`Direct-to-Consumer`), Roku (`Platform`) | ASC 280 segment footnote, top-level reportable segment | Parse segment note, filter on segment-axis member |
| **C — Sub-line inside a bigger segment** | Disney (`Direct-to-Consumer` within `Entertainment`; ESPN DTC within `Sports`) | Intra-segment revenue-category table | Parse segment-detail breakdown, filter the DTC category |
| **D — Disclosed product metric, not a GAAP segment** | Comcast (Peacock, inside NBCU `Media`) | MD&A text / supplemental trending schedules | Targeted text/table mining for product + revenue |
| **E — Not separately disclosed** | Apple (TV+ in `Services`), Amazon (Prime Video in `Subscription services`) | Nowhere in the filing | Flag non-extractable → external estimate / inference |
| **F — Foreign private issuer** | Spotify (Form 20-F, IFRS) | 20-F "Operating & Financial Review" + IFRS notes | Separate 20-F adapter; different taxonomy |

---

## 4. Architecture overview

```
                ┌─────────────────────────────┐
                │  Stage 0: Acquire & structure │
                │  HTML · FilingSummary · XBRL  │
                └───────────────┬───────────────┘
                                │
                ┌───────────────▼───────────────┐
                │  Stage 1: Route sections       │
                │  Items {1,7,8} prior           │
                │  + target Segment / Revenue    │
                │    notes by name               │
                │  + cheap header classifier     │
                └───────┬───────────────┬────────┘
                        │               │
        ┌───────────────▼───┐   ┌───────▼───────────────┐
        │ Stage 2: XBRL      │   │ Stage 3: LLM extractor │
        │ adapter            │   │ prose / fallback       │
        │ deterministic      │   │ full section + labels  │
        │ (PRIMARY)          │   │ (archetype D + verify) │
        └───────────────┬───┘   └───────┬───────────────┘
                        │               │
                ┌───────▼───────────────▼────────┐
                │  Stage 4: Reconcile & score      │
                │  tie to tagged facts; sum check  │
                │  period/stub check; agreement    │
                └───────────────┬─────────────────┘
                                │
                ┌───────────────▼───────────────┐
                │  Stage 5: Emit RevenueSource   │
                │  value + profit + provenance   │
                └───────────────────────────────┘
```

Key properties:

- **Prune before spending LLM calls.** Stage 1 reduces a ~200-page filing to ~3–8 candidate sections using cheap structural filters, so the expensive agents only run where they matter.
- **Never shard below the section.** The LLM extractor always receives a *whole* candidate section plus the segment labels already resolved from the segment note — never a lone header in isolation, because the label it must match is resolved in a different section than the table it reads.
- **Parallelism is per candidate section**, bounded and idempotent (see §11).

---

## 5. Stage detail

### Stage 0 — Acquire & structure

Pull three artifacts before any agent runs:

1. **Filing HTML** — for the narrative Items 1 (Business) and 7 (MD&A).
2. **`FilingSummary.xml`** — EDGAR's own index of the financial-statement R-files. Every statement and note is listed as a discrete report with a short name. This means Item 8 notes do **not** require header-finding — they arrive pre-segmented and named.
3. **XBRL instance** — the tagged facts and their dimensional contexts.

### Stage 1 — Route sections

Three sub-decisions, deterministic where possible:

- **Items.** The set `{1, 7, 8}` is a static prior for every 10-K. No agent needed.
- **Notes.** Do not have an agent "wonder if a note holds revenue." Exactly two notes carry segment/industry revenue and are named predictably:
    - **Segment Reporting** (ASC 280)
    - **Disaggregation of Revenue** (ASC 606)

  Match them by name against `FilingSummary.xml` report short-names (`segment`, `disaggregat`, `revenue`). This is a regex, not an LLM call.
- **Narrative headers (Items 1 & 7).** The only place a cheap classifier earns its keep. Sectionize, score each header for "likely industry revenue" using embeddings against an industry revenue lexicon (or a small/cheap model), keep top-k.

**Output:** ~3–8 candidate sections, each tagged with *why* it was selected.

### Stage 2 — Structured extraction (PRIMARY)

For each candidate that is a note/table backed by XBRL, run the archetype-aware adapter (§6–§7). Deterministic, so parallelize freely. This produces the authoritative number for archetypes A/B/C.

### Stage 3 — LLM extraction (prose / fallback)

For archetype-D prose disclosures (Peacock-style) or where XBRL did not resolve, feed the **entire candidate section plus the resolved segment labels** to an extractor agent. Also used to cross-read the structured number for reconciliation.

### Stage 4 — Reconcile & score

Before any number is trusted:

- Does the extracted figure match a tagged XBRL fact within tolerance?
- Do the disaggregated lines sum to the segment total?
- Does the period context align — and is it a **stub period** (predecessor/successor, see §8)?
- Agreement across paths → high confidence; disagreement → flag, do not silently pick one.

The LLM path *validates* the structured path; it does not compete with it.

### Stage 5 — Emit `RevenueSource`

Write the reconciled value, segment profit if available, period, confidence, and provenance (Item / note / R-file / accession).

---

## 6. Router & registry

```csharp
public enum ReportingArchetype
{
    PurePlaySingleSegment,    // A
    NamedStreamingSegment,    // B
    SubLineWithinSegment,     // C
    DisclosedProductMetric,   // D
    NotDisclosed,             // E
    ForeignPrivateIssuer      // F
}

// One registry entry per known filer. The giants are encoded, not auto-detected —
// this captures the real labels and the renames across years.
public sealed record StreamingFilerProfile
{
    public required string Cik { get; init; }
    public required string Name { get; init; }
    public required ReportingArchetype Archetype { get; init; }

    // For B/C: segment-axis member label(s), newest first.
    // Aliases capture renames so old filings still resolve (e.g. DTC -> Streaming).
    public IReadOnlyList<string> SegmentMemberLabels { get; init; } = [];

    // For C: the parent reportable segment the streaming line lives inside.
    public string? ParentSegment { get; init; }

    // For D: the product name to text-mine.
    public string? ProductLabel { get; init; }

    public string FilingForm { get; init; } = "10-K";   // 20-F for archetype F
    public int FiscalYearEndMonth { get; init; } = 12;   // Disney = 9
}
```

Seed registry (streaming). CIKs should be reconciled against SEC `company_tickers.json` at build time via entity resolution.

```csharp
public static class StreamingRegistry
{
    public static readonly Dictionary<string, StreamingFilerProfile> ByCik = new[]
    {
        new StreamingFilerProfile {
            Cik = "0001065280", Name = "Netflix",
            Archetype = ReportingArchetype.PurePlaySingleSegment },

        new StreamingFilerProfile {
            Cik = "0001437107", Name = "Warner Bros. Discovery",
            Archetype = ReportingArchetype.NamedStreamingSegment,
            SegmentMemberLabels = ["Streaming", "Direct-to-Consumer", "DTC"] },

        new StreamingFilerProfile {
            Cik = "0001744489", Name = "Disney",
            Archetype = ReportingArchetype.SubLineWithinSegment,
            ParentSegment = "Entertainment",
            SegmentMemberLabels = ["Direct-to-Consumer"],
            FiscalYearEndMonth = 9 },              // FY ends late September

        new StreamingFilerProfile {
            Cik = "0002041610", Name = "Paramount Skydance",
            Archetype = ReportingArchetype.NamedStreamingSegment,
            SegmentMemberLabels = ["Direct-to-Consumer", "DTC"] },

        new StreamingFilerProfile {
            Cik = "0001166691", Name = "Comcast",
            Archetype = ReportingArchetype.DisclosedProductMetric,
            ParentSegment = "Media", ProductLabel = "Peacock" },

        new StreamingFilerProfile {
            Cik = "0001428439", Name = "Roku",
            Archetype = ReportingArchetype.NamedStreamingSegment,
            SegmentMemberLabels = ["Platform"] },

        new StreamingFilerProfile {
            Cik = "0001639920", Name = "Spotify",
            Archetype = ReportingArchetype.ForeignPrivateIssuer,
            SegmentMemberLabels = ["Premium", "Ad-Supported"],
            FilingForm = "20-F" },

        new StreamingFilerProfile {
            Cik = "0000320193", Name = "Apple",
            Archetype = ReportingArchetype.NotDisclosed },

        new StreamingFilerProfile {
            Cik = "0001018724", Name = "Amazon",
            Archetype = ReportingArchetype.NotDisclosed },
    }.ToDictionary(p => p.Cik);
}
```

Dispatch:

```csharp
public interface IStreamingRevenueAdapter
{
    ReportingArchetype Handles { get; }
    Task<RevenueExtraction> ExtractAsync(FilingContext ctx, StreamingFilerProfile profile);
}

// Router: registry hit -> archetype adapter; miss -> fallback ladder (see §9).
public async Task<RevenueExtraction> RouteAsync(FilingContext ctx, string cik)
{
    var profile = StreamingRegistry.ByCik.TryGetValue(cik, out var p)
        ? p
        : await ClassifyUnknownAsync(ctx, cik);     // fallback ladder

    var adapter = _adapters.Single(a => a.Handles == profile.Archetype);
    return await adapter.ExtractAsync(ctx, profile);
}
```

---

## 7. Adapters (one method per archetype)

The interface is uniform; the *logic inside* is what differs.

```csharp
public sealed record RevenueExtraction
{
    public decimal? StreamingRevenueUsd { get; init; }
    public decimal? StreamingProfitUsd  { get; init; }   // segment EBITDA / op income
    public string?  GranularitySource   { get; init; }   // "segment_note" | "income_stmt" ...
    public ConfidenceTier Confidence    { get; init; }
    public string?  Evidence            { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}
```

**Archetype A — pure-play.** Streaming *is* the company.

```csharp
public async Task<RevenueExtraction> ExtractAsync(FilingContext ctx, StreamingFilerProfile p)
{
    var total = await ctx.Xbrl.GetFactAsync(
        "us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax",
        dimension: null);                       // flat fact, no segment axis
    return new RevenueExtraction {
        StreamingRevenueUsd = total,
        GranularitySource = "income_statement",
        Confidence = ConfidenceTier.Confirmed };
}
```

**Archetype B — named segment.** Resolve the *current* member label from the footnote (handles renames), then pull revenue scoped to that dimension.

```csharp
public async Task<RevenueExtraction> ExtractAsync(FilingContext ctx, StreamingFilerProfile p)
{
    var member = await ctx.Xbrl.ResolveSegmentMemberAsync(
        axis: "us-gaap:StatementBusinessSegmentsAxis",
        candidateLabels: p.SegmentMemberLabels);          // DTC -> Streaming alias
    if (member is null) return Unresolved(p, "no matching segment member");

    var rev = await ctx.Xbrl.GetFactAsync(
        "us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax",
        dimension: ("us-gaap:StatementBusinessSegmentsAxis", member));

    var profit = await ctx.Xbrl.GetSegmentProfitAsync(member);

    return new RevenueExtraction {
        StreamingRevenueUsd = rev, StreamingProfitUsd = profit,
        GranularitySource = "segment_note",
        Confidence = ConfidenceTier.Confirmed,
        Evidence = $"segment={member.Label}; {ctx.Accession}" };
}
```

**Archetype C — sub-line.** Two-step: parent reportable segment, then the DTC revenue category inside its disaggregation.

```csharp
public async Task<RevenueExtraction> ExtractAsync(FilingContext ctx, StreamingFilerProfile p)
{
    var rev = await ctx.Xbrl.GetFactAsync(
        "us-gaap:RevenueFromContractWithCustomerExcludingAssessedTax",
        dimension:    ("us-gaap:StatementBusinessSegmentsAxis", p.ParentSegment),
        subDimension: ("srt:ProductOrServiceAxis", p.SegmentMemberLabels[0]));

    return new RevenueExtraction {
        StreamingRevenueUsd = rev,
        GranularitySource = "intra_segment_category",
        Confidence = ConfidenceTier.Confirmed,
        Warnings = ["partial: ESPN DTC reported separately under Sports segment"] };
}
```

**Archetype D — disclosed product metric.** No tagged fact → run the LLM extractor over the full MD&A / trending-schedule section plus resolved labels. Confidence capped at `Probable`; sometimes only subscribers/EBITDA-loss are given, not clean revenue.

**Archetype E — not disclosed.** Return `NotDisclosed` immediately so the orchestrator routes to an external estimator. No filing-based confidence.

**Archetype F — foreign private issuer.** Dispatch to a 20-F/IFRS adapter: different form, different taxonomy, "Operating & Financial Review" instead of MD&A, `Premium` / `Ad-Supported` split instead of US-GAAP segments.

---

## 8. Cross-cutting normalizers

These wrap every adapter. They are the difference between a demo and something that survives a year of filings.

- **Fiscal-year misalignment.** Disney closes in late September; most peers at December 31. Align to calendar quarters before any cross-company comparison.
- **Predecessor / successor stub periods.** Mid-year M&A splits a single fiscal year into two non-summing periods (e.g. a January–August predecessor period and an August–December successor period after a merger closes). Detect multiple period contexts on the same concept; decide to sum, present both, or flag.
- **Recast prior periods.** A reorganization republishes prior-year segment numbers under the new structure — often *furnished* as non-GAAP in an 8-K (Item 7.01) rather than *filed* in the 10-K. Prefer recast figures for time series, but tag the liability status.
- **Entity continuity through M&A.** Filers churn (splits, spin-offs, acquisitions). Resolve by `CIK` + `LEI` continuity so a successor entity does not read as a brand-new company with no history.
- **8-K timeliness preference.** The earnings-release exhibit (99.1) carries the segment table weeks before the 10-Q/10-K and is often cleaner. Prefer it for recency; reconcile to the audited 10-K later.

---

## 9. Fallback ladder (unknown filers)

When the registry has no entry, descend until something resolves:

1. **Registry hit** → encoded archetype + labels. Highest confidence.
2. **Auto-classify** → scan the segment footnote for member labels matching a streaming lexicon (`streaming`, `direct-to-consumer`, `DTC`, `platform`, brand names, `+`-suffixed products) → tentative archetype B/C.
3. **Single-segment + streaming-heavy business description** → archetype A.
4. **Product name only in MD&A prose, no XBRL dimension** → archetype D, lower confidence.
5. **Nothing** → archetype E, emit `NotDisclosed`, hand off to external estimation.

---

## 10. Confidence model

```csharp
public enum ConfidenceTier { Confirmed, Probable, Inferred }
```

- **Confirmed** — value comes from a tagged XBRL fact and passes reconciliation (sum check + period check + cross-read agreement).
- **Probable** — value from prose/curated source, or a structured value that failed one reconciliation check.
- **Inferred** — value estimated externally (archetype E) or derived from an input-output prior, not observed in the filing.

A numeric `ConfidenceScore` (0–1) accompanies the tier so downstream shock propagation can decay weak edges (a `Confirmed` single-source figure amplifies; an `Inferred` estimate produces diffuse background signal).

---

## 11. Operational notes

- **Idempotent caching** keyed by `(accession, sectionId)` so re-runs don't re-spend agent calls.
- **Bounded concurrency** on the per-section fan-out. EDGAR requests a declared `User-Agent` and throttles around ~10 requests/second; the Stage 1 pruning is what keeps you well under the ceiling.
- **Provenance is mandatory** on every emitted record: Item / note / R-file / accession / period context. A trading pipeline cannot accept an unattributed number.

---

## 12. Extending to other industries

The reusable scaffold is industry-agnostic:

```
ReportingArchetype enum
  + filer registry
  + uniform adapter interface
  + cross-cutting normalizers
  + fallback ladder
```

For a new sector (semiconductors, banks, airlines, etc.) you rewrite only the **archetype definitions** and the **registry**. The router, normalizers, reconciliation, and ladder stay unchanged. Every sector has the same underlying "same concept, different segment structure" problem.

---

## Appendix — known streaming filers

| Filer | CIK | Archetype | Streaming label(s) | Notes |
|---|---|---|---|---|
| Netflix | 0001065280 | A | (total revenue) | Geo-only disaggregation; membership metrics discontinued 2025 |
| Warner Bros. Discovery | 0001437107 | B | `Streaming` ← `DTC` | Renamed Q1 2025; corporate split in progress |
| Disney | 0001744489 | C | `Direct-to-Consumer` in `Entertainment` | ESPN DTC under `Sports`; FY ends late Sept |
| Paramount Skydance | 0002041610 | B | `Direct-to-Consumer` | Skydance merger → predecessor/successor periods; 3-segment recast |
| Comcast | 0001166691 | D | `Peacock` in `Media` | Not a GAAP segment; product metric only |
| Roku | 0001428439 | B | `Platform` | `Platform` vs `Devices` segments |
| Spotify | 0001639920 | F | `Premium` / `Ad-Supported` | Form 20-F, IFRS |
| Apple | 0000320193 | E | — | TV+ inside `Services`, not broken out |
| Amazon | 0001018724 | E | — | Prime Video inside `Subscription services` |

> CIKs in this appendix are seed values and must be reconciled against SEC `company_tickers.json` at build time. Segment labels change between filings; always resolve the live label from the segment footnote per period rather than trusting the registry blindly.