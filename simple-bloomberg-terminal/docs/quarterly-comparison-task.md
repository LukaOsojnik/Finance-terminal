# Pending task — Quarterly "same-quarter across years" comparison

**Status:** blocked on FMP free-tier data depth. Re-test when the daily quota resets.

## Goal

Under the Details page **QUARTERLY** financial-history view, let the user pick a quarter
(Q1 / Q2 / Q3 / Q4) and then show **every available year compared by that one quarter** — e.g.
pick Q2 → columns `Q2 2026 | Q2 2025 | Q2 2024 | Q2 2023 | …`. This is a seasonal / year-over-year
view that removes seasonality from the comparison.

Must also be covered by the **repopulate / refetch** button (`POST /companies/backfill`) so the
deeper quarterly history gets pulled for existing companies, not just new ones.

## Why it's blocked

FMP free tier caps statement `limit` at **5** (verified 2026-06-09: `limit>5` → HTTP 402
"values for 'limit' must be between 0 and 5"). So a `period=quarter` call returns at most the
**5 most recent quarters** (e.g. AAPL: Q2'26, Q1'26, Q4'25, Q3'25, Q2'25). Picking a single
quarter then yields only **1–2 data points** — not a multi-year comparison.

The current quarterly matrix (metrics × recent quarters) already works; this task is specifically
about **depth** (many years of one quarter), which the 5-record cap prevents.

## What to verify tomorrow (quota reset)

The deciding unknown: **does the free tier honor `from` / `to` date params** (ignoring the limit
cap)? Some FMP endpoints do. Probe (US ticker, no quota waste — one call each):

```
GET /stable/income-statement?symbol=AAPL&period=quarter&from=2018-01-01&to=2025-12-31&apikey=...
GET /stable/income-statement?symbol=AAPL&period=quarter&from=2020-01-01&to=2020-12-31&apikey=...
```

- **If it returns quarters outside the most-recent-5** → deep quarterly history is reachable →
  build the feature (plan below).
- **If it still 402s / caps at 5 / ignores the range** → the feature is **paid-plan only**.
  Document that and stop; the current recent-quarters matrix stays as-is.

Also re-confirm the `limit` ceiling for annual (was 5 → ~5 fiscal years) in case a date range
deepens annual too.

## Build plan (only if `from`/`to` unlocks depth)

1. **Service** (`CompanyFinancialsService`): when fetching quarterly, page by year range using
   `from`/`to` (chunked to stay under the per-call cap and the 250/day quota). Keep the existing
   per-period + best-effort-secondary-statement structure. Store all fetched quarters as
   `CompanyFinancial` rows (schema already supports it — keyed by `FiscalYear` + `Period`).
2. **Client** (`IFmpApiClient` / `FmpApiClient`): add `from`/`to` params to the statement methods.
3. **Backfill** (`POST /companies/backfill`): no API change — it reuses `BuildAsync`, so deeper
   quarterly flows through automatically. Note: eligibility is "has FMP financials"; to *re-deepen*
   companies already filled with shallow quarterly, either widen eligibility or add a "force refresh"
   flag.
4. **Details view** (`Views/Companies/Details.cshtml`): under the QUARTERLY tab, add a Q1–Q4
   picker. On selection, filter `Model.Financials` to `Period == chosen` and render the transposed
   matrix with one column per `FiscalYear` (newest left), reusing the existing `metrics` array and
   formatters.

## Watch-outs

- **Quota math:** deep quarterly multiplies FMP calls per company (each `from`/`to` chunk = its own
  call × 4 statements). The backfill already stops on 429; keep that. May span more days.
- **429 vs 402:** the service already lets 429 (daily quota) bubble and only swallows 402 (gated) —
  preserve that so deep-fetch failures don't silently degrade to Yahoo. See
  `CompanyFinancialsService.BuildAsync` / `TryList`.
- Yahoo quarterly (`incomeStatementHistoryQuarterly`) is also shallow (~4 quarters) and has gutted
  line items, so it's not a depth substitute for non-US companies.

_Captured 2026-06-09. Related: `docs/external-api.md`, FMP free-tier notes in memory._
