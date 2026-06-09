# Impact engine — upgrade ideas

Data / API integrations that add value to the impact engine (`docs/impact-engine.md`). Each is
tied to a specific gap in the current state, ordered by value-to-effort.

---

## ① BEA API — finer & current matrix `A`  ✅ DONE

**Status:** implemented. The committed model is now built from the **BEA 2024 summary Use table**
(TableID 259, auto-discovered, latest year) via `build_io_model.py --source bea-api`. Validated:
ρ(A)=0.48, cond≈2.15, all invariants pass. The 2002 detail build remains as an offline fallback.
Remaining option below is finer granularity (detail/benchmark) if Health/IT need pharma/software split.

**Original gap:** the committed model was BEA **2002** detail, aggregated to **11** sectors.

**Adds:**
- Current-year data instead of 2002.
- Finer `n` — summary (71) or detail (~400 industries). Lets you shock *oil & gas* rather than all
  of ENERGY, *autos* rather than all of Consumer Discretionary. The math is unchanged; only the
  matrix grows (brief Section 8: "only `n` grows").

**Effort:** low — already scaffolded. `Scripts/build_io_model.py --source bea-api` reads
`appsettings.json` `Bea:ApiKey`. May need a small concordance tweak for summary letter-codes.

---

## ② FRED API — calibrate magnitude to real moves  (free, St. Louis Fed)  ★ best value/effort

**Gap:** magnitude is typed by hand ("derive from your head"). No link to reality.

**Adds — turns guesses into real shock sizes:**
- **Commodity spot prices** (oil, gas, metals) → size an energy/materials **cost** shock from the
  actual % move.
- **PPI by sector** → real cost-push magnitudes per sector.
- **Fed funds / real rates** → capital-cost shocks (the `capital` component of `v`).
- **Sector GDP / gross output** → refresh baseline output and `d`, scale **demand** shocks in real $.

**Outcome:** "oil +30% last quarter → this `Δv`" instead of `-1000`. Could prefill the magnitude
field from a chosen indicator.

**Effort:** low — free key, simple JSON series endpoint. Typed `HttpClient` like the existing
FMP/Perplexity clients.

---

## ③ Exploit the existing counterparty graph — firm-level exposure  (no new API)

**Gap:** company allocation is sector-average × revenue share ("Amazon = 42% of the sector").

**Adds:** the app already collects firm-to-firm links — `RevenueSource`/`CostSource.RelatedCompanyId`
plus Perplexity counterparty discovery and filing extraction. That is the seed of a **firm-level
`A`** (brief Section 8): real Amazon→supplier edges instead of a sector average. The I-O math is
identical — `n` grows, matrix becomes sparse.

**Effort:** medium–high (it's the real phase-2), but uses data you already gather. Start by building
a firm-level sub-matrix for companies that have ≥1 linked counterparty.

---

## ④ EIA API — real energy intensity  (free, Energy Information Administration)

**Gap:** `IoModel.Energy[]` is all zeros (energy is endogenous via `A`); there is no per-sector
energy-intensity vector.

**Adds:** EIA energy-consumption-by-sector → a real energy-intensity vector, so a cost-push energy
shock can be weighted by *how energy-hungry each sector actually is* — the brief's flagship example
(Section 1.4). Would make `energy[]` a genuine, separately-shockable primary input.

**Effort:** low–medium — free key; needs a mapping from EIA sectors to the 11 GICS buckets, and a
decision on reconciling it with the accounting identity (`colsum(A)+v=1`).

---

## ⑤ FMP (already integrated) — better company weighting

**Gap:** allocation weights by stored `RevenueTotal` only; cost shocks don't use cost structure.

**Adds:** FMP already provides market cap and gross margin. Weight by **cost base**
(`revenue × (1 − GrossMargin)`) for cost shocks, or by market cap for exposure. Makes the
per-company price-shock number meaningful instead of omitted.

**Effort:** very low — data already fetched; it's an allocation-formula change in
`EventImpactService`.

---

## ⑥ UN Comtrade / US Census trade API — tariff / embargo shocks  (free)

**Gap:** the engine has no trade dimension, yet the app already has `Country`, `TradeBloc`, `Event`.

**Adds:** export/import shares by sector → model **tariffs / embargoes / sanctions** as a demand
shock on the *export slice* of a sector's final demand, weighted by real trade exposure. Directly
connects the impact engine to existing geopolitical Events.

**Effort:** medium — needs trade-by-sector data and a split of `d` into domestic vs export.

---

## ⑦ Event → shock auto-mapping  (uses existing DeepSeek / Perplexity)

**Gap:** the brief intends an `Event` to map to a shock; today it's manual.

**Adds:** feed an `Event` (headline/description) to the LLM already wired in the app → it proposes
`{origin sector, kind, magnitude}`, which the adapter runs. Closes the loop "real event → cascade"
end-to-end.

**Effort:** medium — prompt + adapter wiring; reuses existing LLM clients.

---

## Recommended sequence

1. **① BEA API** — finer/current matrix (key in hand, path scaffolded).
2. **② FRED** — calibrate magnitudes to real prices/rates (small, free, fixes the biggest UX gap).
3. **③ counterparty graph** — firm-level exposure (the phase-2 prize, data already collected).

①+② together turn the tool from a sandbox into **real event → real shock → real cascade**.
