# Impact engine — current state

The analytical core of the event-impact feature: a **linear, static input–output model** that
traces how a shock to one sector cascades across the 11 GICS sectors, ranks who is hit, and lists
the companies sitting in each affected sector. Implements the brief in `io-core-implementation.md`.

End-user flow: **pick origin sector → choose shock type → set magnitude → see ranked sector
cascade + per-sector companies + round-by-round trace.** Live at `/impact`.

---

## 1. Architecture

```
IoCore/                         pure math + data (no EF, no domain types beyond the Sector enum)
  SectorIndex.cs                canonical ordering: Sector enum ordinal == matrix index
  IoModel.cs                    immutable holder: A, labor/energy/capital/taxes, d
  IoSolver.cs                   Math.NET LU: L=(I-A)^-1, Ghosh G, price dual, condition number
  IoShock.cs                    round-by-round A^k decomposition (traceability)
  IoMetrics.cs                  output multipliers, centrality, ranked-most-affected
  IoModelLoader.cs              load JSON → validate every invariant (fails app startup if broken)
  Data/io_model_v1.json         the committed 11×11 model (built from real BEA data)
  Data/concordance_bea_to_gics_v1.json   audit trail: which BEA code → which GICS sector

Services/EventImpactService.cs  the only domain↔core adapter: request → Δd/Δv/Δw, solve,
                                map back to sectors, allocate to companies, derive sector profiles
Controllers/ImpactController.cs GET /impact (page) + POST /impact/solve (JSON)
Views/Impact/Index.cshtml       controls, Chart.js cascade, company drill-down, trace panel
Scripts/build_io_model.py       offline builder: BEA Use table → io_model_v1.json
```

`LoadedIoModel` (model + solver) is a **singleton**, validated once at startup.
`EventImpactService` is **scoped** (reads companies via the request-scoped DbContext).

---

## 2. The math (all linear, two independent solves sharing `L`)

`A[i,j]` = units of sector i required per unit of sector j's output. `L = (I-A)^-1` (Leontief
inverse) is obtained by LU-solving `(I-A)X = I` — we solve linear systems, never form an explicit
inverse.

| Shock type | Direction | Solver | Output |
|---|---|---|---|
| **Demand** | backward (pull) | Leontief quantity `Δx = L·Δd` | Δ gross output per sector |
| **Supply** | forward (push) | Ghosh quantity `Δxᵀ = Δwᵀ·G` | Δ gross output per sector |
| **Cost** | cost-push | Leontief price dual `Δpᵀ = Δvᵀ·L` | Δ unit price/cost per sector |

Quantity and price are never coupled into a fixed point (that would be CGE — out of scope). No
substitution elasticities; coefficients in `A` are fixed.

**Traceability:** `IoShock` walks the Neumann series `Δx = (I + A + A² + …)·Δd` term by term, so
every flagged sector is explainable by *how far down the chain* it sits (round 0 = direct shock,
round 1 = direct suppliers, …). The cost dual walks `Aᵀ`.

---

## 3. Data

`io_model_v1.json` is built from the **BEA 2024 summary Use table** (InputOutput dataset, TableID
259, "The Use of Commodities by Industries — Summary", ~71 industries) aggregated to the 11 GICS
sectors by `Scripts/build_io_model.py --source bea-api`. The script auto-discovers the table and
the latest year via the BEA API.

- **Concordance** (NAICS-prefix → GICS) is an approximate modelling decision — GICS classifies by
  equity market, NAICS by production. Resolved mapping (66 codes) is in
  `concordance_bea_to_gics_v1.json` for audit. Key calls: farming → Consumer Staples;
  mining (ex oil&gas) → Materials; autos → Cons. Discretionary, aerospace → Industrials;
  computer/electronic + computer-design + data-processing → IT; broadcasting/publishing/arts →
  Comm Services; food stores → Cons. Staples; construction → Industrials.
- **Summary-level limitations** (inherent to 71-industry resolution; the 2002 detail build avoided
  them): pharmaceuticals are inside `325 Chemical` → Materials, medical devices inside `339` →
  Discretionary, and software inside `511 Publishing` → Comm Services — none separable at summary
  level, so Health Care and IT are under-weighted vs a detail build. Government industries are
  excluded (not GICS sectors).
- **Aggregation:** both commodity (row) and industry (col) BEA codes collapse into the same 11
  buckets → square 11×11. `A[i,j] = z[i,j]/output[j]`.
- **Value added** split into `labor` (compensation), `capital` (gross operating surplus), `taxes`.
  Because everything is normalised by output, `colsum(A) + v = 1` by construction → baseline
  price fixture `p = 1` holds.
- **Energy is endogenous** — it stays the ENERGY sector inside `A`, so `IoModel.Energy[]` is all
  zeros. An "energy cost shock" is a `Δv` bump on the origin sector; it propagates correctly
  because `A` already encodes who uses energy.
- **Final demand** `d = x − A·x` (Leontief identity) → guarantees `L·d = x` with `x > 0`.

Validated: all column sums of A < 1 (Hawkins–Simon), ρ(A) = 0.48, condition #(I−A) ≈ 2.15,
baseline `p = 1`, multipliers ≥ 1, linearity, zero-shock⇒zero-response.

**Rebuild / refresh:** `python Scripts/build_io_model.py --source bea-api` (needs `Bea:ApiKey` in
`appsettings.json`; auto-picks TableID 259 + latest year). `--list-tables` prints all InputOutput
tables for the key. The offline fallback `--source detail2002 --file IOUseDetail.txt` builds from
the 2002 benchmark detail (~400 industries) — finer (separates pharma/medical/software) but old.
The loader fails app startup loudly if any invariant breaks.

---

## 4. UI behaviour

- **Soft sector guidance:** picking a sector shows its data-derived orientation and final-demand
  share (`d[s]/x[s]`, computed at runtime), and defaults the shock type — upstream sectors
  (Materials −8%, Utilities 28%, Energy 31%) → Supply; demand-facing (Health 99%, Cons. Disc. 79%,
  Comms 68%, Real Estate 57%) → Demand; mixed (~43–53%: Industrials, IT, Staples, Financials) →
  Demand. Override allowed; Cost applies to any sector.
- **Ranked sectors** as a Chart.js horizontal bar + table; click a row to expand the companies in
  that sector.
- **Cascade trace** panel: per round, the top movers as proportional bars + signed values.
- **Magnitude semantics:** Demand/Supply = absolute $ (BEA scale, $ millions); Cost = a fraction
  (0.10 = +10% unit cost). Because the model is linear, magnitude only scales results — the
  ranking and cascade shape are scale-invariant.

---

## 5. Company allocation (phase-2, simplest variant)

Each affected sector's impact is split across the companies in it (`Company.Sector`):
- **Quantity shock:** company Δ = sector Δ × revenue share within the sector.
- **Cost shock:** per-company $ is *not* estimated (it depends on each firm's cost structure and
  pass-through); companies are listed by revenue share only, with the sector Δp shown.
- Companies with no `RevenueTotal` split the residual equally so they still appear.

**GICS-industry sub-grouping (drill-down only):** within each sector the companies are further
grouped by `Company.Industry` into an `IndustryImpact` list (the finer "who's hit" view). Each
group's Δ is the sum of its members' Δ — additive, so a sector's total still equals the sum of its
industries; the matrix math and the sector-level weights are untouched. Companies with no `Industry`
fall under "Unclassified". For cost shocks the industry header is label-only (no fabricated $). The
flat per-company list is kept alongside `Industries` for the sector-row count.

---

## 6. Guardrails (do NOT cross)

- Fixed coefficients — no substitution / CES / nonlinear production (that's CGE).
- No coupled price–quantity fixed point — two independent linear solves sharing `L`.
- `labor / energy / capital` kept separable in `v` — never collapsed.
- Solve linear systems, do not invert explicitly.
- Traceability preserved — a black-box ranking is a defect.
- Event→perturbation mapping stays in the adapter, out of `IoCore`.
