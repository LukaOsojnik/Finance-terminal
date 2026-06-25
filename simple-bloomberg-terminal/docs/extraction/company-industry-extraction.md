# Company industry extraction (FMP label → GICS sub-industry → industry)

Status: **implemented.** Describes how a company's GICS classification is produced and stored.

## The three tiers

A company carries its industry classification at three levels of resolution, finest first:

| Field on `Company` | Tier | Filled by |
|--------------------|------|-----------|
| `FmpIndustry` (string?) | raw vendor label | stored **verbatim** from the FMP profile (`profile.industry`) / sonar |
| `GicsSubIndustry` (enum, 163) | GICS sub-industry | the **LLM** — the single judgment step |
| `Industry` (enum, 74) | GICS industry | **deterministic rollup** of the sub-industry — no LLM |

GICS is a strict hierarchy — **Sub-Industry → Industry → Industry Group → Sector** — so once the
sub-industry is known, the industry (and sector) fall out for free. The LLM therefore does exactly
**one** thing: pick the sub-industry. `GicsSubIndustryExtensions.GetIndustry()` (and `GetSector()`)
do the rollup. `Company.Industry` is a denormalized cache of that rollup, kept because the rest of
the app filters/groups on it.

Why store the raw `FmpIndustry` at all: the old pipeline ran `FmpMapper.MapIndustry(label)` and
**threw the label away** — a map miss left `null` and no record of what the vendor said. Persisting
it makes the pipeline *recoverable*: industry can be re-resolved later (better LLM, a corrected
cache) without re-hitting FMP's rate-limited API, and it's the strongest signal we can give the LLM.

## The resolver — `IIndustryClassifier.ResolveSubIndustryAsync(sector, fmpLabel, name, ct)`

Every path below funnels through this one method. It is a three-tier cascade:

1. **Cache** (`FmpIndustryMapping` table, keyed by normalized label). A vendor label maps to the
   same sub-industry for *every* company, so the first time a label is seen it's mapped and
   persisted; every later company with that label resolves from the DB with **no LLM call**.
2. **LLM** (cheap/`fast` tier, e.g. DeepSeek flash) on a cache miss. The prompt is constrained to
   the sub-industries that belong to the given **Sector** (so a stray pick can't cross sectors),
   and the parsed result is re-checked against the sector. `maxTokens` is generous because the
   flash tier spends output tokens reasoning before the JSON — a tight budget truncates the reply
   and yields a false "no fit." On a null it logs the precise reason (truncated / model-said-null /
   out-of-list / wrong-sector / malformed).
3. **Persist** the freshly-learned `label → sub-industry` into the cache, so it's free next time.

When there is no label (a name-only company), the cache is skipped and the LLM resolves from
**name + sector** alone — weaker, but it keeps ticker-less companies classified.

The result is rolled up by the caller: `company.GicsSubIndustry = sub; company.Industry = sub?.GetIndustry();`

---

## Flow 1 — Standard company add (synchronous, at the time of adding)

The New Company form, private-company discovery, and counterparty linking all resolve **immediately**
so the user sees the classification right away.

**New Company by ticker** (`CompaniesController.Fetch` → `BuildFromTickerAsync` → `TickerProfileEnricher`):

```
FMP profile ─▶ FmpMapper.ToCreateModel
                 ├─ FmpIndustry = profile.industry          (raw, stored verbatim)
                 └─ Sector      = MapSector(profile.sector)  (FMP sector → GICS sector)
            ─▶ ResolveSubIndustryAsync(Sector, FmpIndustry, Name)   ← cache, else cheap LLM
                 └─ GicsSubIndustry
            ─▶ Industry = GicsSubIndustry.GetIndustry()       (deterministic rollup)
```

- Runs at **Fetch**, synchronously: one LLM call, or zero on a cache hit. The form is prefilled with
  the resolved Industry; the Create POST round-trips `FmpIndustry` + `GicsSubIndustry` + `Industry`
  as hidden inputs so the saved entity keeps all three.
- **Manual add (no ticker):** no FMP fetch, no LLM — the user picks Sector / Industry by hand;
  `FmpIndustry` and `GicsSubIndustry` stay null.
- **Private (web discovery)** (`BuildPrivateAsync`): sonar's free-text industry string becomes the
  `FmpIndustry` label and is resolved the same way.
- **Counterparty link** (`GetOrCreateCounterpartyAsync`): the FMP branch resolves label-based via the
  enricher; the ticker-less stub resolves from name + sector.

---

## Flow 2 — Index import + Backfill (deferred / bulk)

Importing an index (SPDR / Wikipedia) can provision hundreds of companies, so the LLM step is
**deferred** off the fast provisioning path.

**Provision** (`CompanyProvisioningService.CreateFromProfileAsync`, phase 1, ~1 FMP call):

- Stores just the membership-relevant fields **+ the raw `FmpIndustry` label**. **No LLM** here —
  `GicsSubIndustry` / `Industry` are left null so the import stays fast and the breakdown opens promptly.

**Enrich** (`EnrichAsync`, phase 2, background, right after the import connects members):

- For each just-provisioned company, resolves the sub-industry from the **stored** `FmpIndustry`
  (cache, else cheap LLM) and rolls up to `Industry`. No FMP re-fetch needed — the label is already on
  the row.

**Backfill** — two independent sweeps on the Companies page, each its own button + service method, both
sharing the detached-job / live-progress / cancel machinery:

- **Backfill financials** (`BackfillFinancialsAsync`) — FMP-bound, the **expensive** sweep: for companies
  without FMP financials yet, re-fetch the profile + dated financial history (Yahoo fallback) — ~5 FMP
  calls/company (profile + four statement endpoints). Stops cleanly on the FMP daily 429 so a re-run
  tomorrow resumes.
- **Resolve industries** (`BackfillIndustriesAsync`) — the **call-cheap, higher-priority** sweep,
  self-contained per company (below).

The industries sweep resolves the sub-industry for every company still missing one
(`GicsSubIndustry == null`), and gets its **own** label so it never depends on the financials backfill.
Per company, two steps:

1. **Ensure a label** — if `FmpIndustry` is null, fetch the FMP **profile** (one call) and store it.
2. **Resolve** — label → GICS sub-industry (cache, else cheap LLM) → roll up to `Industry`.

So a previously-unclassified company costs **one FMP profile call + one LLM call** (or zero FMP if the
label is already stored, and zero LLM if the label is already in the cache) — far cheaper than routing
through the ~5-call financials backfill. Industry is ranked above financials, so this is the sweep to run
first when FMP quota is scarce.

A company we can't get a label for — no SEC ticker, or the FMP daily quota is already spent — is
**skipped**, not weak-guessed from name + sector (those guesses were poor and quota-blind). On the 429
the sweep stops fetching *new* labels but keeps resolving rows that already carry one (cache/stored
labels need no FMP call); the summary reports how many were skipped so a re-run tomorrow can finish them.

Backfill runs **detached** with live progress: the page polls a status endpoint and streams one line
per company as it resolves; the Close button cancels the run (the token aborts the in-flight LLM
request and stops the loop, keeping whatever resolved so far).

> Note: the Backfill key is `GicsSubIndustry == null`. Because that column is new (null on every existing
> row), the first run processes **all** companies lacking a sub-industry — not only the previously-shown-as
> -unclassified ones — and **overwrites** the existing `Industry` with the rollup of the newly-resolved
> sub-industry. A wrong stored `Sector` still constrains the candidate list, so a mislabelled-sector
> company may resolve to "no fit" until its sector is corrected.

---

## Where the LLM runs vs. not

| Add path | FMP/vendor label | Sub-industry reasoning |
|----------|------------------|------------------------|
| New Company — ticker Fetch | stored | **now**, at Fetch (LLM or cache; shown in form) |
| New Company — manual | none | none — user picks by hand |
| Private (web discovery) | sonar label | now, at discovery |
| Counterparty link | FMP or none | now (label-based, or name+sector for a stub) |
| Index import — provision | stored | **deferred** to Enrich |
| Index import — Enrich / Backfill | stored (label captured at provision / financials backfill) | bulk, cache-deduped |

The cache (`FmpIndustryMapping`) is shared across **all** paths: a label first mapped during an index
Backfill resolves with zero LLM cost the next time it appears on a New Company Fetch, and vice-versa.
After a run, `SELECT DISTINCT FmpIndustry FROM Companies` + the mapping table show the exact,
auditable label → sub-industry set the vendor produced for your universe.
