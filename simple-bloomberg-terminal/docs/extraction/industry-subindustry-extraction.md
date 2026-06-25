# Industry / sub-industry extraction (vendor label → GICS sub-industry → industry / sector)

Status: **implemented.** Describes the GICS classification pipeline — how a company's sub-industry is
reasoned, how Industry and Sector roll up from it, and the recent two-stage / self-heal / correction
changes. Companion to [`company-industry-extraction.md`](./company-industry-extraction.md) (the
per-path flow map) and [`company-info-extraction.md`](./company-info-extraction.md) (the FMP cost tiers).

## Inputs

Two sources feed the classifier, both giving the same shape — a coarse **sector** string, a finer
**industry label** string, and a free-text **description**:

| Source | Sector | Industry label | Description | Path |
|--------|--------|----------------|-------------|------|
| FMP profile | `profile.Sector` (~11 strings) | `profile.Industry` (string) | `profile.Description` | ticker fetch / index import / backfill |
| Perplexity sonar | `r.Sector` (GICS enum name, often) | `r.Industry` (free text) | `r.Description` | private-company discovery (`BuildPrivateAsync`) |

The **LLM (the user's pro model)** is the classifier — it does exactly one judgment: pick the GICS
sub-industry. Everything coarser is then derived, not asked.

## Mapping to GICS

The vendor strings are mapped onto our enums before (and after) the LLM step:

- **Sector:** `FmpMapper.MapSector` maps FMP's ~11 sector strings (`SectorMap`, normalized key) to the
  GICS `Sector` enum. An **unknown sector → `null`** (unclassified) — *not* a default. (The old
  non-nullable `Sector` defaulted to the enum's value 0 = `ENERGY`, so any sector-less company silently
  looked like an energy company; `null` can't masquerade as a real sector.) The private path tries the
  exact GICS enum name first (`Enum.TryParse<Sector>`), then falls back to `FmpMapper.MapSector`, else `null`.
- **Industry label:** the FMP/sonar industry string is stored **verbatim** on `Company.FmpIndustry`
  (`p.Industry.Trim()`). It is the label-cache key **and** the classifier's strongest signal. There is
  **no** hand-maintained label→industry dictionary.
- **Sub-industry:** reasoned by the LLM (the single judgment step).
- **Industry + Sector roll up deterministically** from the chosen sub-industry via
  `GicsSubIndustry.GetIndustry()` and `GicsSubIndustry.GetSector()`. Because the sub is authoritative, a
  successful classify **self-heals** a wrong or missing FMP sector — the provisionally-mapped sector is
  overwritten by `sub.GetSector()`.

GICS is a strict hierarchy, so once the sub-industry is known, Industry and Sector fall out for free.
The code models **three tiers** — `GicsSubIndustry → GicsIndustry → Sector` — collapsing GICS's
Industry-Group tier: `GicsSubIndustry.GetIndustry()` then `GicsIndustry.GetSector()` map straight through.

## The two-stage classifier

`IIndustryClassifier.ResolveSubIndustryAsync(sector, fmpLabel, companyName, description, bypassCache, ct)`
funnels every path. Internally: **label-cache → `ClassifyAsync` (stage 1 → stage 2) → cache write**.

1. **Label cache** (`FmpIndustryMapping`, keyed by `Normalize(label)`). A vendor label maps to the same
   sub-industry for *every* company, so a known label resolves from the DB with **no LLM call**. The
   read is **skipped when `bypassCache`** is set (the "Resolve with AI" path always re-reasons).
2. **`ClassifyAsync` → `PickAsync`** — `PickAsync` is one model call; `ClassifyAsync` runs it up to twice:
   - **Stage 1 — constrained.** Only when a (trusted) source sector is known: the candidate list is
     restricted to that sector's sub-industries (`GetSector() == sec`), and the parsed pick is re-checked
     against the sector (a wrong-sector pick is rejected). A guardrail so a stray pick can't cross sectors.
   - **Stage 2 — unconstrained.** Reached when there was **no source sector at all**, *or* stage 1 found
     **no fit**. The candidate list is **all** sub-industries; the model reasons from name + description +
     label and the chosen sub defines its own sector. This is what corrects a vendor sector that disagrees
     with GICS (FMP files solar under Energy, but GICS has no solar sub-industry there — it rolls up under
     IT/Utilities).
   - **Both stages use the pro model (`fast: false`).** Flash was tempting for the constrained one-of-N
     stage 1, but it made silent *sibling* errors (a plausible neighbour in the right sector) that land as
     a confident "Resolved" and — being cached by label — become permanent. `maxTokens: 900` gives the
     reasoning model headroom before the JSON; a tight budget truncates mid-reasoning → empty → null.
3. **Cache write — only a clean stage-1 hit.** `ClassifyAsync` returns a `Cacheable` flag: `true` only for
   a constrained stage-1 match. A **stage-2 fallback is never cached** — it was driven by *this* company's
   name/description because the label was ambiguous or misleading (e.g. FMP labels Public Storage
   "REIT - Industrial"), so caching it by label would mis-map the next company sharing that label. A
   `bypassCache` re-resolve also never writes (it's a per-company judgement, not a label rule).

On a miss `PickAsync` logs the precise reason (truncated / model-said-null / out-of-list / wrong-sector /
malformed). A missing key or unreachable model just returns `null` — industry is best-effort and never
blocks the flow.

## Population order per entry path

The constant is: **Sector is set provisionally from the vendor, then overwritten by `sub.GetSector()` on a
successful classify** (the sub is authoritative). The paths differ only in *when* the LLM runs.

| Path | Method | Order |
|------|--------|-------|
| New Company — ticker fetch | `TickerProfileEnricher.BuildModelAsync` | map (`FmpIndustry`, `Sector` provisional) → `ResolveSubIndustryAsync` → `Industry = sub.GetIndustry()` → **`Sector = sub.GetSector()`** on a hit |
| Private / sonar discovery | `BuildPrivateAsync` | parse Sector / `FmpIndustry` → classify → roll up Industry **and** Sector from the sub |
| Index import — fast provision | `CreateFromProfileAsync` | store `FmpIndustry` + provisional Sector only; **no LLM** — `GicsSubIndustry`/`Industry` left null |
| Index import — enrich | `EnrichAsync` | for each member with `GicsSubIndustry == null`: classify from the stored label, then `ApplyClassification` rolls up Industry + Sector |
| Counterparty stub (no ticker) | `GetOrCreateCounterpartyAsync` | name-only classify from `(sec, null, name)`; on a hit roll up; on a miss leave **Pending** (`markNoFitOnMiss: false`) for a later backfill |

`ApplyClassification(company, sub)` is the shared roll-up: a hit sets `GicsSubIndustry`, `Industry =
sub.GetIndustry()`, `Sector = sub.GetSector()`, `ClassifyStatus = Resolved`; a miss sets `NoFit` (unless
`markNoFitOnMiss: false`, which leaves it `Pending`).

## Fallbacks

- **Classification fallback is LLM-only:** constrained → unconstrained, both pro. There is no static
  dictionary fallback.
- **Yahoo is a fallback for FINANCIALS only** (revenue / gross margin, via `ApplyYahooFinancials` when FMP
  income is premium-gated) — **never** for classification.
- **Missing FMP label** → classify by name + description (no cache key, so a name-only result is never cached).
- **Missing sector** → skip stage 1, go straight to the unconstrained stage 2.
- **Missing CIK / ticker** → no FMP label is fetchable, so a name-only classify runs. The CIK backfill
  (`BackfillCiksAsync`, pure SEC name match — no FMP/LLM/quota) can recover a US filer's CIK, after which a
  later sweep can fetch the real label and re-resolve.

## State and correction

- **`ClassifyStatus` {Pending, Resolved, NoFit}** (`Company.ClassifyStatus`, default `Pending`):
  `Pending` = never attempted; `Resolved` = a sub was assigned; `NoFit` = the classifier ran (constrained
  *then* unconstrained) and still found nothing. `BackfillIndustriesAsync` processes only
  `GicsSubIndustry == null && ClassifyStatus != NoFit && !ClassificationLocked` — a `NoFit` row isn't
  weak-guessed again on every sweep; it waits for an explicit AI re-resolve.
- **Unclassified report** (`/companies/unclassified`, `Unclassified` action): every active company with
  `GicsSubIndustry == null`, `NoFit` first then `Pending`. Each row offers "Resolve with AI".
- **Classification review** (`/companies/classification`, `Classification` action): every active company
  with its raw label beside the resolved Sector / Industry / Sub, so a human can catch a confident mis-fit
  (which is marked "Resolved" and never shows on the Unclassified list). A normalized FMP label that
  resolved to **more than one** distinct sub-industry across the book is **Flagged** as ambiguous /
  colliding (the cache can't disambiguate it). Flagged first, then by sector.
- **`ClassificationLocked`** (`Company.ClassificationLocked`): a human pinned this classification.
  `BackfillIndustriesAsync` skips locked rows and `ReclassifyAsync` returns early on them — neither the
  auto-backfill nor the AI re-resolve overwrites a manual fix. The **Edit form** exposes the sub-industry
  select (and the lock checkbox); when the sub is set there, `ApplyEdit` makes it authoritative and rolls
  Industry + Sector up from it (`sub.GetIndustry()` / `sub.GetSector()`), ignoring the coarser dropdowns.
- **Cache eviction on manual edit:** when the Edit POST changes the sub-industry (`subChanged`), the
  controller calls `_mappings.Remove(oldLabel)` — the row's label produced a wrong / ambiguous cached
  mapping, so the poisoned entry is forgotten and other companies sharing the label get re-reasoned fresh.
- **"Resolve with AI"** (`Reclassify` action → `ReclassifyAsync`): fetches a missing FMP label first, then
  runs the full constrained → unconstrained classify with **`bypassCache: true`** so it re-reasons instead
  of echoing the cached / ambiguous value. Always reattempts (even a prior `NoFit`) and self-heals
  Sector / Industry from the pick. Returns the fresh classification as JSON so the row updates in place.

## Recent changes (2026-06-25)

- **Nullable `Sector`.** `Company.Sector` is now `Sector?`; **`NULL` = unclassified**. It was previously
  non-nullable and defaulted to enum value 0 (`ENERGY`), silently mislabelling every sector-less company.
- **Two-stage classifier** with an **unconstrained stage-2 fallback** — reached on no-sector or a stage-1
  no-fit — replacing the old single sector-constrained pass that returned "no fit" whenever the vendor
  sector disagreed with GICS.
- **Self-heal sector from the sub.** A successful classify overwrites the provisional vendor sector with
  `sub.GetSector()` (via `ApplyClassification` / the enricher / `BuildPrivateAsync`).
- **Both stages on the pro model** (`fast: false`) — flash made permanent, cached sibling errors.
- **Cache hygiene:** only a clean stage-1 hit is `Cacheable`; `bypassCache` skips read **and** write;
  `Remove` evicts a poisoned label when a human corrects a sub by hand.
- **`ClassifyStatus`** (Pending / Resolved / NoFit) and **`ClassificationLocked`** added to `Company`.
- **Review + Unclassified pages** (`/companies/classification`, `/companies/unclassified`) plus the
  per-row "Resolve with AI" action.

---

The three classification outputs are **`Company.GicsSubIndustry`**, **`Company.Industry`**, and
**`Company.Sector`** — all derived from the resolved sub-industry (Industry = `GetIndustry()`, Sector =
`GetSector()`). On a miss, `GicsSubIndustry`/`Industry` stay null and `Sector` keeps the FMP-mapped value
(or null when the vendor sector was unknown).
