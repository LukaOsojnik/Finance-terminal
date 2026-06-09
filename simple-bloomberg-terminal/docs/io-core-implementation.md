# Implementation brief: linear input–output core

## 0. Context and objective

We are building the analytical core of an event-impact engine. Given a macro or
geopolitical event, the engine must trace its effect through a sector dependency
graph and rank which sectors (and later, companies) are hit, and how.

The core is **linear and static**. This is a deliberate choice, not a limitation:
linearity gives us closed-form solutions, no parameters to invent, fast recompute,
and — critically — **traceability** (we can show the path a shock took). Do not
replace it with an equilibrium solver. See Section 7 (Guardrails) before writing
any code.

The core has exactly three solvers, all built on the same matrix:

| Event type | Direction | Solver | Output |
|---|---|---|---|
| Demand collapse / demand surge | backward (pull) | Leontief quantity | Δ gross output per sector |
| Supply outage / capacity loss | forward (push) | Ghosh quantity | Δ gross output per sector |
| Cost shock (oil, wages, rates) | cost-push | Leontief price (dual) | Δ unit cost / price per sector |

Two separate linear solves. We never solve price and quantity simultaneously —
that is CGE and is explicitly out of scope.

---

## 1. Mathematical specification

Let `n` = number of sectors. All vectors are length `n`, matrices are `n × n`.
Canonical sector ordering is fixed once (Section 2) and is the single source of
truth for every index.

### 1.1 Technical coefficients matrix `A`

`A[i][j]` = units of sector *i*'s output required to produce one unit of sector
*j*'s output. **Column `j` is the input recipe for sector `j`.** These are fixed
constants (the Leontief assumption).

### 1.2 Leontief quantity model (demand-pull, backward)

```
(I - A) x = d
x = (I - A)^(-1) d = L d
```

- `L = (I - A)^(-1)` is the **Leontief inverse** (total-requirements matrix).
  `L[i][j]` = total output of *i* needed, directly and indirectly, per unit of
  final demand for *j*.
- Demand shock: `Δx = L Δd`.
- Output multiplier of sector `j` = column sum `Σ_i L[i][j]`.

### 1.3 Ghosh quantity model (supply-push, forward)

`B[i][j]` = sales from *i* to *j* per unit of *i*'s **output** (row-normalised
allocation coefficients: `B[i][j] = z[i][j] / x[i]`).

```
xᵀ = wᵀ (I - B)^(-1) = wᵀ G
```

- `G = (I - B)^(-1)` is the **Ghosh inverse**.
- `w` = primary inputs (value added) per sector.
- A supply-side disruption to sector `k` (loss of a primary input) propagates
  **downstream**: `Δxᵀ = Δwᵀ G`.
- Use this when the event is an upstream outage (e.g. a supplier goes offline),
  not a fall in final demand.

### 1.4 Leontief price model (cost-push) — the piece people forget

This is the **dual** of the quantity model and is what lets us handle "prices
move" without going to CGE.

```
pᵀ = pᵀ A + vᵀ
pᵀ (I - A) = vᵀ
p = (I - A)^(-ᵀ) v = Lᵀ v
```

- `v` = **value added per unit of output**, decomposed into the three primary
  inputs (see 2.3): `v[j] = labor[j] + energy[j] + capital[j] (+ taxes[j])`.
- At baseline, normalise so `p = 1` (all unit prices = 1). This is a regression
  fixture (Section 6).
- Cost shock: `Δpᵀ = Δvᵀ L`. An energy price spike enters by bumping the energy
  component of `v` across sectors (weighted by each sector's energy intensity),
  and `L` propagates it into every sector's unit cost.
- Margin read: sectors with large `Δp` that cannot pass cost through are the
  margin-compression candidates.

---

## 2. Data model

### 2.1 Sectors

Fixed canonical list (GICS 2023, 11 sectors). The full industry breakdown exists
but **phase 1 operates at the 11-sector level**. Keep `sectorIndex` (string ↔ int)
as one immutable mapping.

```
ENERGY, MATERIALS, INDUSTRIALS, CONSUMER_DISCRETIONARY, CONSUMER_STAPLES,
HEALTH_CARE, FINANCIALS, INFORMATION_TECHNOLOGY, COMMUNICATION_SERVICES,
UTILITIES, REAL_ESTATE
```

### 2.2 Matrix `A`

Source from a published I-O table (US BEA make/use tables, OECD ICIO, WIOD, or
EXIOBASE). **Caveat the Coder must surface:** these tables are classified by
NAICS/ISIC, *not* GICS. A NAICS→GICS concordance is required and is approximate —
it is a modelling decision, not a lookup. Store the concordance as a versioned
config artifact (`concordance/naics_to_gics.json`) so it can be audited and
swapped.

For bootstrapping before real data lands, generate a synthetic `A` that satisfies
the productivity condition (Section 6): non-negative, column sums strictly < 1,
diagonally reasonable. This unblocks the solver and tests immediately.

### 2.3 Value-added intensities (the three primary inputs)

Per sector `j`, store separately — do **not** collapse into a single scalar:

- `labor[j]`   — labor cost per unit output (the "people" input)
- `energy[j]`  — energy cost per unit output (the "gas/sun/grid" input)
- `capital[j]` — capital / financing cost per unit output (the "money/banks" input)

Keeping them separable is what makes factor-specific shocks possible (wage shock,
energy shock, rate shock all hit different components). `v[j]` is their sum.

### 2.4 Final demand `d`

Length-`n` vector of final consumption per sector. The baseline `d` reproduces the
benchmark `x` when multiplied by `L`.

---

## 3. Event representation

An event is **a perturbation to one of three things**: `d`, a row/column of `A`,
or `v`. Keep the domain→perturbation translation in an adapter layer, completely
separate from the math core.

```
Event {
  kind: DEMAND | SUPPLY | COST
  targetSectors: [sectorId...]
  // DEMAND: Δd on target sectors
  // SUPPLY: capacity loss → Δw (Ghosh) or scaling of target columns/rows of A
  // COST:  Δv split across {labor, energy, capital} on target sectors
  magnitude / vector
}
```

The adapter produces a clean `Δd`, `Δv`, or `Δw`; the core consumes only those.
The core must never know what "an oil embargo" is — only that `energy` component
of `v` rose by some amount on some sectors.

---

## 4. Suggested module layout

```
core/
  matrices      build & validate A, B; build labor/energy/capital/v; build d
  solve         L = solve(I - A);  G = solve(I - B);  price solve (I - A)^T p = v
  shock         apply Δd / Δv / Δw → return Δx, Δp
  metrics       output multipliers, centrality, ranked most-affected
adapters/
  events        map domain Event → perturbation vector
mapping/
  concordance   NAICS↔GICS  (config-driven, versioned)
  companies      sector↔company exposure weights (phase 2)
```

---

## 5. Implementation order (for the Coder)

1. Build `sectorIndex` and load/validate `A`.
2. **Productivity check** (Section 6) — fail loudly if it doesn't hold.
3. Compute `L` by **solving the linear system** `(I - A) X = I`, not by calling an
   explicit matrix-inverse routine. (.NET: Math.NET Numerics LU; Python:
   `numpy.linalg.solve` / `scipy.linalg.lu_solve`.)
4. Quantity demand-pull: `x = L d`; shock `Δx = L Δd`.
5. Price model: solve `(I - A)^T p = v`.
6. Ghosh: build `B`, compute `G`, supply-push `Δxᵀ = Δwᵀ G`.
7. Metrics: column-sum multipliers; centrality (column sums of `L`, or Katz /
   eigenvector on `A`); ranked impact list per event.
8. Event adapter mapping the three `Event.kind`s to perturbations.
9. (Phase 2) company allocation — Section 8.

---

## 6. Validation and invariants (for the Reviewer)

Economic and numerical sanity checks. These are unit tests, not optional:

- **Non-negativity:** every entry of `A` ≥ 0.
- **Productivity / Hawkins–Simon:** each column sum of `A` < 1 (intermediate input
  share below 1; the remainder is value added). Equivalently spectral radius
  `ρ(A) < 1`, which guarantees `L = Σ A^k` converges and `L ≥ 0`.
- **Leontief inverse shape:** all `L[i][j] ≥ 0`; diagonal `L[i][i] ≥ 1`.
- **Baseline price fixture:** set `v = (I - A)^T · 1`; the price solve must return
  `p = 1` (all ones) to numerical tolerance. Lock this as a regression test.
- **Zero shock ⇒ zero response:** `Δd = 0 ⇒ Δx = 0`; `Δv = 0 ⇒ Δp = 0`.
- **Multipliers ≥ 1:** every column sum of `L` ≥ 1.
- **Linearity:** `Δx(α·Δd) == α·Δx(Δd)` within tolerance.
- **GDP identity (balanced table):** total value added ≈ total final demand.
- **Hand-checked toy case:** a 2- or 3-sector matrix solved by hand must match the
  code to tolerance. Keep it as the canonical fixture.

---

## 7. Guardrails — do NOT do these

These are the boundary between our linear core and the "heavier" paradigms we are
deliberately avoiding. Crossing them silently is the main failure mode.

- **No substitution elasticities, no CES, no nonlinear production functions.**
  Coefficients in `A` are fixed. The moment inputs substitute in response to
  prices, you are building CGE — out of scope.
- **No simultaneous price–quantity equilibrium solve.** Quantity and price are two
  independent linear solves that share `L`. Do not couple them into a fixed point.
- **No agent behavior / emergent dynamics.** No per-firm behavioral rules — that is
  ABM, out of scope.
- **Preserve traceability.** Keep the shock's propagation path recoverable (e.g.
  retain the round-by-round `A^k Δd` contributions, or a path decomposition), so
  every flagged sector is explainable. A black-box result is a defect here.
- **Do not invert explicitly** for large/sparse systems — solve linear systems.
- **Keep `labor / energy / capital` separable** in `v`. Never collapse them, or
  factor-specific shocks become impossible.
- **Keep the event→perturbation mapping out of the math core.**

If the thesis ever genuinely requires second-order reallocation (a tariff causing
substitution *away* from a supplier, not just a cost rise), that is the *only*
case that justifies a CGE module — and even then, bolt a small one onto this core
for the single sector, do not rebuild.

---

## 8. Numerical notes and phase-2 company extension

**Numerical**
- Solve via LU on `(I - A)`; log the condition number, warn if ill-conditioned.
- Single canonical sector ordering everywhere; index↔sector is one mapping.
- .NET: Math.NET Numerics (`Matrix<double>`, LU). Python: NumPy/SciPy; use sparse
  matrices once at company scale.

**Company level (phase 2)**
- Simplest: map each company to its sector, then split that sector's `Δ` across
  member companies by an exposure weight (revenue share, or a company-level
  sensitivity factor). Company impact = sector Δ × exposure weight.
- Richer: build a true firm-level `A` from supply-chain data (FactSet Supply Chain
  Relationships, Bloomberg SPLC). The math is identical — only `n` grows and the
  matrix becomes sparse.

---

## 9. Definition of done (phase 1)

- `A`, `v` (split into labor/energy/capital), and `d` load and pass all Section 6
  invariants.
- All three solvers (`Δx` demand-pull, `Δx` supply-push, `Δp` cost-push) implemented
  and tested against the hand-checked toy fixture.
- An `Event` of each kind flows through the adapter and produces a ranked,
  explainable list of most-affected sectors.
- Baseline price fixture (`p = 1`) and linearity tests green.