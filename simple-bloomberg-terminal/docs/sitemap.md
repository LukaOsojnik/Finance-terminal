# Sitemap

## /

| Field | Value |
|---|---|
| Controller | HomeController |
| Action | Index |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`, defaults: Home/Index) |
| View | Views/Home/Index.cshtml |
| Parameters | — |

---

## /Home/Privacy

| Field | Value |
|---|---|
| Controller | HomeController |
| Action | Privacy |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`) |
| View | Views/Home/Privacy.cshtml |
| Parameters | — |

---

## /countries

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("countries")]` + `[Route("")]` |
| View | Views/Countries/Index.cshtml |
| Parameters | — |

---

## /countries/search

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("countries")]` + `[Route("search")]` |
| View | Views/Countries/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /countries/lookup

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Lookup |
| HTTP | GET |
| Route source | `[Route("countries")]` + `[Route("lookup")]` |
| View | — (JSON `{id,label}` array, up to 10 matches) |
| Parameters | term (query string) |
| Notes | Backs the autocomplete picker |

---

## /countries/create

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("countries")]` + `[Route("create")]` |
| View | Views/Countries/Create.cshtml |
| Parameters | form fields (POST) |

---

## /countries/{id}/overview

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("countries")]` + `[Route("{id:long}/overview")]` |
| View | Views/Countries/Details.cshtml |
| Parameters | id: long (route, constrained) |

---

## /countries/{id}/edit

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("countries")]` + `[Route("{id:long}/edit")]` |
| View | Views/Countries/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /countries/{id}/delete

| Field | Value |
|---|---|
| Controller | CountriesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("countries")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /companies

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("")]` |
| View | Views/Companies/Index.cshtml |
| Parameters | — |

---

## /companies/search

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("search")]` |
| View | Views/Companies/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /companies/lookup

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Lookup |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("lookup")]` |
| View | — (JSON `{id,label}` array, up to 10 matches) |
| Parameters | term (query string) |
| Notes | Backs the autocomplete picker |

---

## /companies/create

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("companies")]` + `[Route("create")]` |
| View | Views/Companies/Create.cshtml |
| Parameters | form fields (POST) |

---

## /companies/fetch

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Fetch |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("fetch")]` |
| View | Views/Companies/Create.cshtml (returned as view name "Create") |
| Parameters | symbol: string? (form) |
| Notes | Prefills the New Company form from the Financial Modeling Prep API by ticker symbol, then returns the Create view with the mapped model for review before the user submits the normal `POST /companies/create`. Persists nothing itself |

---

## /companies/discover-private

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | DiscoverPrivate |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("discover-private")]` (ValidateAntiForgeryToken) |
| View | Views/Companies/Create.cshtml (returned as view name "Create") |
| Parameters | name: string (form) |
| Notes | The "PRIVATE · AI" tab of the New Company page: web-searches (Perplexity sonar) a private company's profile by name and returns the Create view prefilled with Type=PRIVATE plus AI-estimated sector/industry/country/description and estimated revenue & gross margin, for the user to review before submitting the normal `POST /companies/create`. Persists nothing itself |

---

## /companies/backfill

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Backfill |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("backfill")]` (ValidateAntiForgeryToken) |
| View | — (JSON `{ filled:[{name,ticker,rows,source}], failed:[{name,ticker,reason}], rateLimited:bool, remaining:int, message }`) |
| Parameters | — |
| Notes | Bulk-refreshes existing companies from the real APIs: resolves a ticker from each company's SEC CIK (via the SEC company_tickers.json map), skips companies that already have FMP-sourced financials, then re-runs the profile+financials fetch (overwriting AI-seeded profile fields and replacing the dated CompanyFinancial history). Stops cleanly when FMP's daily quota (HTTP 429) is reached so a later run resumes. Driven by the "⟳ BACKFILL FROM APIs" button + results modal on the Companies index page |

---

## /companies/backfill/ciks

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | BackfillCiks |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("backfill/ciks")]` (ValidateAntiForgeryToken) |
| View | — (JSON `{ jobId }` — starts a detached background backfill job, no redirect; results render in the existing Companies/Index backfill popup) |
| Parameters | — |
| Notes | Sibling to `backfill/financials` and `backfill/industries`. Launches a detached `BackfillJob` (registered in the singleton in-memory `BackfillJobStore`) that assigns SEC CIKs to US companies missing one (FMP returns null for some US filers) by matching each company's name against the SEC ticker map — a fast, quota-free pure name match needing no FMP/LLM keys. Returns the `jobId` at once; the popup polls `backfill/{id}/status` for live per-company progress + the final summary, and the close button cancels via `backfill/{id}/cancel` |

---

## /companies/{id}/profile

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("{id:long}/profile")]` |
| View | Views/Companies/Details.cshtml |
| Parameters | id: long (route, constrained) |

---

## /companies/{id}/edit

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/edit")]` |
| View | Views/Companies/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |
| Notes | The form exposes a GICS Sub-Industry select that is authoritative — when set, Industry + Sector roll up from it (the coarser Sector/Industry picks are ignored); cleared, they fall back to the explicit Sector/Industry. A "Lock classification" checkbox pins the row so the backfill/AI re-resolve leaves it alone. Changing the sub-industry forgets the row's cached FMP-label mapping so a wrong/ambiguous label can't keep mis-classifying others |

---

## /companies/{id}/rediscover

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Rediscover |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/rediscover")]` (named route `CompanyRediscover`, ValidateAntiForgeryToken) |
| View | — (JSON `{ jobId }`, HTTP 200 — fire-and-forget, no redirect) |
| Parameters | id: long (route, constrained) |
| Notes | Asynchronous re-discovery for an existing PRIVATE company: registers a detached `RediscoverJob` in the singleton in-memory `RediscoverJobStore`, then fires the Perplexity `sonar-pro` profile discovery + mapping + save (overwriting profile fields and re-stamping the single estimated financial row) on a background `Task.Run` with a fresh DI scope, and returns the `jobId` at once so the request doesn't block. Public companies are rejected with 400 BadRequest; missing company → 404. Triggered by the "RE-DISCOVER (PERPLEXITY)" button on the Details view (Views/Companies/Details.cshtml) for private companies; the browser hands the `jobId` to the bottom-right notification widget, which polls `GET /extraction/scan-jobs`. When the job finishes, if the user is still on that company's Details page it auto-reloads to show the updated profile + financials |

---

## /companies/{id}/linked-sources

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | LinkedSources |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("{id:long}/linked-sources")]` |
| View | — (JSON `{ owned, inverse }` arrays of `{ kind, direction, id, name, type, value, other }`) |
| Parameters | id: long (route, constrained) |
| Notes | The company's active revenue/cost sources in two groups: `owned` (CompanyId == id — these block deletion) and `inverse` (RelatedCompanyId == id — owned by other companies). Feeds the linked-sources delete popup on the Companies index page. Returns 404 when the company is not found |

---

## /companies/{id}/delete

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/delete")]` (named route `CompanyDelete`) |
| View | — (soft-delete; returns status codes, not a redirect) |
| Parameters | id: long (route, constrained) |
| Notes | Driven by fetch in site.js, so it returns 200 OK on success and 409 Conflict (with the blocking message) when active linked sources prevent deletion, rather than redirecting |

---

## /companies/{id}/ingest-volume

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | IngestVolume |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/ingest-volume")]` (named route `CompanyIngestVolume`, Authorize, ValidateAntiForgeryToken) |
| View | — (JSON `{ count, series }`) |
| Parameters | id: long (route, constrained) |
| Notes | Ingests the company's weekly trading-volume history from Yahoo Finance (range=max) and stores it, then returns the stored series. Triggered by the "INGEST VOLUME (YAHOO)" button on the Company Details page (Views/Companies/Details.cshtml) to populate the weekly volume graph next to the Financial Overview / COMPANY DATA card. Returns 404 when the company is not found; 422 when the company has no SEC ticker (non-US) or Yahoo returns no data |

---

## /companies/classification

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Classification |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("classification")]` (`[AllowAnonymous]`) |
| View | Views/Companies/Classification.cshtml |
| Parameters | — |
| Notes | The classification review: a table of EVERY active company showing its raw FMP source label beside the resolved Sector / Industry / Sub-industry, so a human can catch a confident mis-fit (which is marked Resolved and never appears on `/companies/unclassified`). Rows whose normalized FMP label resolved to more than one distinct sub-industry across the book are flagged (ambiguous/colliding label the cache can't disambiguate); flagged rows sort first, then by sector then name. Renders `List<ClassificationRow>`. Client-side filters: All / Unclassified / Flagged. Each row offers a "✦ AI" re-resolve (POSTs to `/companies/{id}/reclassify`) and an EDIT link. Linked from the "⚑ CLASSIFICATION" link on the Companies index page (Views/Companies/Index.cshtml) — it replaced the old "⚑ UNCLASSIFIED" link, though `/companies/unclassified` still exists |

---

## /companies/unclassified

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Unclassified |
| HTTP | GET |
| Route source | `[Route("companies")]` + `[Route("unclassified")]` (`[AllowAnonymous]`) |
| View | Views/Companies/Unclassified.cshtml |
| Parameters | — |
| Notes | The Unclassified report: lists every active company with no resolved GICS sub-industry, NoFit first (the genuine misses the backfill flagged) then Pending, sorted by name. Each row offers a "Resolve with AI" action that POSTs to `/companies/{id}/reclassify`. The Companies index page no longer links here directly (its "⚑ CLASSIFICATION" link now points at `/companies/classification`), but the route remains live |

---

## /companies/{id}/reclassify

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Reclassify |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/reclassify")]` (ValidateAntiForgeryToken) |
| View | — (JSON `{ resolved, status, sector, industry, subIndustry }`) |
| Parameters | id: long (route, constrained) |
| Notes | On-demand AI re-resolve of one company's GICS sub-industry (the "Resolve with AI" button on the Unclassified page). Runs inline on the user's own keys (FMP label fetch + the cheap classifier); the classifier swallows a missing key and returns null, so a key-less account sees "couldn't place" rather than an error. Returns the fresh classification (AJAX, called from `/companies/unclassified`) so the row updates in place. Returns 404 when the company is not found |

---

## /events/feed

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("events")]` + `[Route("feed")]` |
| View | Views/Events/Index.cshtml |
| Parameters | — |
| Notes | Returns flat `IEnumerable<EventRowViewModel>` (previously split into live/past) |

---

## /events/search

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("events")]` + `[Route("search")]` |
| View | Views/Events/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /events/create

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("events")]` + `[Route("create")]` |
| View | Views/Events/Create.cshtml |
| Parameters | form fields (POST) |

---

## /events/{id}/summary

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("events")]` + `[Route("{id:long}/summary")]` |
| View | Views/Events/Details.cshtml |
| Parameters | id: long (route, constrained) |

---

## /events/{id}/edit

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("events")]` + `[Route("{id:long}/edit")]` |
| View | Views/Events/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /events/{id}/delete

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("events")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /trade-blocs

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("trade-blocs")]` + `[Route("")]` |
| View | Views/TradeBlocs/Index.cshtml |
| Parameters | — |

---

## /trade-blocs/search

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("trade-blocs")]` + `[Route("search")]` |
| View | Views/TradeBlocs/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /trade-blocs/create

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("trade-blocs")]` + `[Route("create")]` |
| View | Views/TradeBlocs/Create.cshtml |
| Parameters | form fields (POST) |

---

## /trade-blocs/{id}/overview

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("trade-blocs")]` + `[Route("{id:long}/overview")]` |
| View | Views/TradeBlocs/Details.cshtml |
| Parameters | id: long (route, constrained) |

---

## /trade-blocs/{id}/edit

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("trade-blocs")]` + `[Route("{id:long}/edit")]` |
| View | Views/TradeBlocs/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /trade-blocs/{id}/delete

| Field | Value |
|---|---|
| Controller | TradeBlocsController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("trade-blocs")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /country-details

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("country-details")]` + `[Route("")]` |
| View | Views/CountryDetails/Index.cshtml |
| Parameters | — |

---

## /country-details/search

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("country-details")]` + `[Route("search")]` |
| View | Views/CountryDetails/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /country-details/validate-country

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | ValidateCountry |
| HTTP | GET |
| Route source | `[Route("country-details")]` + `[Route("validate-country")]` |
| View | — (JSON boolean: true if country has no CountryDetails row yet) |
| Parameters | countryId: long (query string) |
| Notes | Used by the unobtrusive `[Remote]` AJAX uniqueness check on `CountryDetailsCreateModel.CountryId` picker |

---

## /country-details/create

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("country-details")]` + `[Route("create")]` |
| View | Views/CountryDetails/Create.cshtml |
| Parameters | form fields (POST) |

---

## /country-details/{countryId}/edit

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("country-details")]` + `[Route("{countryId:long}/edit")]` |
| View | Views/CountryDetails/Edit.cshtml |
| Parameters | countryId: long (route, constrained — 1:1 with Country, PK = CountryId); form fields (POST) |

---

## /country-details/{countryId}/delete

| Field | Value |
|---|---|
| Controller | CountryDetailsController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("country-details")]` + `[Route("{countryId:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | countryId: long (route, constrained — 1:1 with Country, PK = CountryId) |

---

## /country-advantages

| Field | Value |
|---|---|
| Controller | CountryAdvantagesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("country-advantages")]` + `[Route("")]` |
| View | Views/CountryAdvantages/Index.cshtml |
| Parameters | — |

---

## /country-advantages/search

| Field | Value |
|---|---|
| Controller | CountryAdvantagesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("country-advantages")]` + `[Route("search")]` |
| View | Views/CountryAdvantages/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /country-advantages/create

| Field | Value |
|---|---|
| Controller | CountryAdvantagesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("country-advantages")]` + `[Route("create")]` |
| View | Views/CountryAdvantages/Create.cshtml |
| Parameters | form fields (POST) |

---

## /country-advantages/{id}/edit

| Field | Value |
|---|---|
| Controller | CountryAdvantagesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("country-advantages")]` + `[Route("{id:long}/edit")]` |
| View | Views/CountryAdvantages/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /country-advantages/{id}/delete

| Field | Value |
|---|---|
| Controller | CountryAdvantagesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("country-advantages")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /country-challenges

| Field | Value |
|---|---|
| Controller | CountryChallengesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("country-challenges")]` + `[Route("")]` |
| View | Views/CountryChallenges/Index.cshtml |
| Parameters | — |

---

## /country-challenges/search

| Field | Value |
|---|---|
| Controller | CountryChallengesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("country-challenges")]` + `[Route("search")]` |
| View | Views/CountryChallenges/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /country-challenges/create

| Field | Value |
|---|---|
| Controller | CountryChallengesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("country-challenges")]` + `[Route("create")]` |
| View | Views/CountryChallenges/Create.cshtml |
| Parameters | form fields (POST) |

---

## /country-challenges/{id}/edit

| Field | Value |
|---|---|
| Controller | CountryChallengesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("country-challenges")]` + `[Route("{id:long}/edit")]` |
| View | Views/CountryChallenges/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /country-challenges/{id}/delete

| Field | Value |
|---|---|
| Controller | CountryChallengesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("country-challenges")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /gdp-snapshots

| Field | Value |
|---|---|
| Controller | GdpSnapshotsController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("gdp-snapshots")]` + `[Route("")]` |
| View | Views/GdpSnapshots/Index.cshtml |
| Parameters | — |

---

## /gdp-snapshots/search

| Field | Value |
|---|---|
| Controller | GdpSnapshotsController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("gdp-snapshots")]` + `[Route("search")]` |
| View | Views/GdpSnapshots/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /gdp-snapshots/create

| Field | Value |
|---|---|
| Controller | GdpSnapshotsController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("gdp-snapshots")]` + `[Route("create")]` |
| View | Views/GdpSnapshots/Create.cshtml |
| Parameters | form fields (POST) |

---

## /gdp-snapshots/{id}/edit

| Field | Value |
|---|---|
| Controller | GdpSnapshotsController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("gdp-snapshots")]` + `[Route("{id:long}/edit")]` |
| View | Views/GdpSnapshots/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /gdp-snapshots/{id}/delete

| Field | Value |
|---|---|
| Controller | GdpSnapshotsController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("gdp-snapshots")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /revenue-sources

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("revenue-sources")]` + `[Route("")]` |
| View | Views/RevenueSources/Index.cshtml |
| Parameters | — |

---

## /revenue-sources/search

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("revenue-sources")]` + `[Route("search")]` |
| View | Views/RevenueSources/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /revenue-sources/create

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("revenue-sources")]` + `[Route("create")]` |
| View | Views/RevenueSources/Create.cshtml |
| Parameters | form fields (POST) |

---

## /revenue-sources/{id}/breakdown

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/breakdown")]` |
| View | Views/RevenueSources/Details.cshtml |
| Parameters | id: long (route, constrained) |
| Notes | Single-source management page: shows the source's editable fields (inline edit form posting to Edit) plus its per-field proof reviews with their connected filings, and lets the user detach a field's proof or replace which filing backs it. ViewModel: `RevenueSourceDetailViewModel` |

---

## /revenue-sources/{id}/reviews/{reviewId}/detach

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | DetachReview |
| HTTP | POST |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/reviews/{reviewId:long}/detach")]` |
| View | — (soft-deletes one SourceFieldReview, redirects to breakdown Details) |
| Parameters | id: long (route, constrained); reviewId: long (route, constrained) |
| Notes | Detaches one field's proof by soft-deleting that SourceFieldReview for the source |

---

## /revenue-sources/{id}/reviews/{reviewId}/filing

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | SetReviewFiling |
| HTTP | POST |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/reviews/{reviewId:long}/filing")]` |
| View | — (redirects to breakdown Details) |
| Parameters | id: long (route, constrained); reviewId: long (route, constrained); filingId: long? (form/query) |
| Notes | Sets/clears that review's FilingId (replace which filing backs the field; null clears it) and resets the phase-2 verdict |

---

## /revenue-sources/{id}/edit

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/edit")]` |
| View | Views/RevenueSources/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST); returnUrl: string? (query, POST) |
| Notes | POST also accepts an optional `returnUrl` and redirects there if it is a local URL (so the breakdown page's inline edit returns to itself); otherwise redirects to Index |

---

## /revenue-sources/{id}/delete

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /cost-sources

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("cost-sources")]` + `[Route("")]` |
| View | Views/CostSources/Index.cshtml |
| Parameters | — |

---

## /cost-sources/search

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Search |
| HTTP | GET |
| Route source | `[Route("cost-sources")]` + `[Route("search")]` |
| View | Views/CostSources/_TableBody.cshtml (partial, AJAX) |
| Parameters | search query (query string) |

---

## /cost-sources/create

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Create |
| HTTP | GET, POST |
| Route source | `[Route("cost-sources")]` + `[Route("create")]` |
| View | Views/CostSources/Create.cshtml |
| Parameters | form fields (POST) |

---

## /cost-sources/{id}/breakdown

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("cost-sources")]` + `[Route("{id:long}/breakdown")]` |
| View | Views/CostSources/Details.cshtml |
| Parameters | id: long (route, constrained) |

---

## /cost-sources/{id}/edit

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("cost-sources")]` + `[Route("{id:long}/edit")]` |
| View | Views/CostSources/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

---

## /cost-sources/{id}/delete

| Field | Value |
|---|---|
| Controller | CostSourcesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("cost-sources")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

---

## /indices

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("indices")]` + `[Route("")]` (`[AllowAnonymous]`) |
| View | Views/Indices/Index.cshtml |
| Parameters | — |
| Notes | Returns an `IndicesPageViewModel` (indices + recent import jobs) instead of `IEnumerable<StockIndex>`. Lists the tracked `StockIndex` rows grouped by sector and sorted by size (Σ member market cap), plus the recent import jobs. The old `ViewBag.Catalog` of hardcoded one-click import indices has been removed — there are no longer hardcoded catalog import buttons |

---

## /indices/{id}/breakdown

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Details |
| HTTP | GET |
| Route source | `[Route("indices")]` + `[Route("{id:long}/breakdown")]` (`[AllowAnonymous]`) |
| View | Views/Indices/Details.cshtml |
| Parameters | id: long (route, constrained) |
| Notes | Shows the index's sector + industry breakdown — grouped live from each member's `Company.Sector` / `Company.Industry`, weighted by the stored per-member `IndexConstituent.WeightPct` — plus the constituent list. ViewModel: `IndexDetailViewModel`. Returns 404 when the index is not found |

---

## /indices/discover

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Discover (async) |
| HTTP | POST |
| Route source | `[Route("indices")]` + `[Route("discover")]` (ValidateAntiForgeryToken) |
| View | — (JSON array of DiscoveredIndex: code, name, wikiPage, region, sector, etf_ticker) |
| Parameters | query: string (form); ct: CancellationToken |
| Auth | `[Authorize(Roles = "Admin,Manager")]` (class-level) |
| Notes | Web-searches the stock-market indices matching a free-text query via Perplexity sonar (`IIndexDiscovery`). Each suggestion carries its English-Wikipedia constituents-page path plus a sector and ETF ticker so the page can render one-click import buttons that POST to `/indices/import`. Persists nothing. 400 if query blank; 424 if the user has no Perplexity key |

---

## /indices/import

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Import |
| HTTP | POST |
| Route source | `[Route("indices")]` + `[Route("import")]` (ValidateAntiForgeryToken) |
| View | — (JSON `{ jobId }` — starts a detached background import job, no redirect) |
| Parameters | code: string?, name: string?, wikiPage: string?, etfTicker: string?, sector: string?, region: string? (all form) |
| Auth | `[Authorize(Roles = "Admin,Manager")]` |
| Notes | Starts a detached background import job (registered in the in-memory `IndexImportJobStore`) and returns its `jobId` at once so the slow Wikipedia scrape / SPDR fetch + SEC map can't time out the request; the page polls `import/{jobId}/status`. Routes to the SPDR holdings source for real weights when an ETF ticker is known, else scrapes Wikipedia membership (cap-weighted). Used by both the Perplexity-discovered suggestion buttons (code+wikiPage+sector+etfTicker) and the manual SPDR ETF import box (etfTicker only). 400 if neither a code nor an ETF ticker is provided |

---

## /indices/import/{id}/continue

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Continue (async) |
| HTTP | POST |
| Route source | `[Route("indices")]` + `[Route("import/{id:long}/continue", Name = "StockIndexImportContinue")]` (ValidateAntiForgeryToken) |
| View | — (JSON `{ jobId }` — flips the job to Running and re-runs the detached import, no redirect) |
| Parameters | id: long (route, constrained) |
| Auth | `[Authorize(Roles = "Admin,Manager")]` (class-level) |
| Notes | Continues a job whose FMP auto-provisioning was cut short (Partial) — or retries one that errored — under THIS user's API keys. Re-runs the stored `IndexImportRequest`: existing members re-link for free via the SEC CIK map, and only the still-missing members spend the continuing user's FMP quota. Any authorized user may continue any user's job (stamps `ContinuedBy`). Returns 404 when the job id is unknown; 400 when the job is already Running |

---

## /indices/import/{id}/status

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | ImportStatus |
| HTTP | GET |
| Route source | `[Route("indices")]` + `[Route("import/{id:long}/status")]` |
| View | — (JSON `{ status, progress, indexId, message, error }`) |
| Parameters | id: long (route, constrained) |
| Auth | `[Authorize(Roles = "Admin,Manager")]` (class-level) |
| Notes | Polls one import job's live status. The page polls it to show progress and, on success, link to the breakdown (`indices/{indexId}/breakdown`) of the index that was just imported. Returns 404 when the job id is unknown |

---

## /indices/{id}/delete

| Field | Value |
|---|---|
| Controller | IndicesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("indices")]` + `[Route("{id:long}/delete", Name = "StockIndexDelete")]` (ValidateAntiForgeryToken) |
| View | — (soft-delete, redirects to Index) |
| Parameters | id: long (route, constrained) |
| Auth | `[Authorize(Roles = "Admin,Manager")]` |
| Notes | Soft-deletes the index and redirects to Index |

---

## /extraction

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("")]` |
| View | Views/Extraction/Index.cshtml |
| Parameters | companyId: long? (query string, optional); revenueSourceId: long? (query string, optional — REVENUE deep-link prefill only); node: string? (query string, optional — `REVENUE`\|`COST`\|`RISK`, default `REVENUE`) |
| Notes | Phase-1 split-screen extraction UI — company autocomplete picker plus a Node dropdown (Revenue/Cost/Risk) that drives which SEC Items are scanned (Revenue/Cost → Items 7,8; Risk → Items 1A,7A), the AI prompts, and which entity a save lands in (RevenueSource / CostSource / CompanyRisk). Left pane = source cells, right pane = JSON from `POST /api/stock/refresh/{companyId}`; "Use as reference" writes per-cell SourceFieldReview rows. `?revenueSourceId=` deep-links prefill an existing RevenueSource row (REVENUE node only). The page JS also reads `accession`, `doc`, `form`, and `jobId` from the query string (not bound server-side) to rehydrate the filing/scan context when the user clicks back from the notification widget to save a background scan's yielded objects |

---

## /extraction/references/{sourceId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | References |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("references/{sourceId:long}")]` |
| View | — (JSON array of per-field references: field, snapshot, pointer, endpoint, mark, rationale, filing) |
| Parameters | sourceId: long (route, constrained); node: string? (query string, optional — `REVENUE`\|`COST`\|`RISK`, default `REVENUE`; picks which source table/FK the id resolves against) |
| Notes | Existing references for a source row, so the extraction page can show each cell's pointer on load. Returns 404 when the source row id is not found for the given node |

---

## /extraction/reference

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Reference |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("reference")]` |
| View | — (JSON `ReferenceResult` record: RevenueSourceId, ReviewId, Field) |
| Parameters | JSON body (ReferenceRequest) — now also carries `node` (`REVENUE`\|`COST`\|`RISK`, default `REVENUE`) and `note` (RISK rows) |
| Notes | Ensures the node's source row exists (RevenueSource / CostSource / CompanyRisk; create if new, DataSource=MANUAL) then upserts one SourceFieldReview per (row, Field) with the matching RelationKind and Mark=null; re-referencing resets the verdict. Responses: 200 OK; 400 (missing companyId/name/snapshot, or invalid classification); 404 (source row id not found). Superseded in the UI by `POST /extraction/save` (endpoint still present but no longer called) |

---

## /extraction/save

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Save |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("save")]` |
| View | — (JSON `{ revenueSourceId, proofs }`) |
| Parameters | JSON body (SaveRequest): companyId, revenueSourceId?, node (`REVENUE`\|`COST`\|`RISK`, default `REVENUE`), sourceType, name, value, percentage, note, relatedCompanyId, proofs:[{field, endpoint, referencePointer, referenceSnapshot, referencedValue, filingAccessionNumber, filingForm, filingDate, filingUrl}] |
| Notes | Current UI save path — saves the whole extraction form in one request: `node` selects which entity the row upserts into (RevenueSource / CostSource / CompanyRisk; `note` backs RISK rows) from the field values, then upserts one SourceFieldReview per entry in Proofs. Responses: 200 OK; 400 (bad companyId / missing name / invalid classification); 404 (revenueSourceId given but row not found) |

---

## /extraction/save-batch

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | SaveBatch (async) |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("save-batch")]` |
| View | — (JSON `{ saved, links }`) |
| Parameters | JSON body — { companyId, node, accession, form, items:[{ name, classification, value, percentage, note, relatedCompany, relatedCompanyTicker, proof:{ name, value, percentage, classification, relatedCompany, note } }] } |
| Notes | Batch-saves multiple AI-proposed objects in one call from the notification widget's chat (the user ticks which `​```save```​` blocks to keep). For each item it upserts the source row (RevenueSource / CostSource / CompanyRisk) plus per-field SourceFieldReview proof (endpoint "AI extraction", pointer "ai-suggested"). Items that name a related company (revenue→CUSTOMER, cost→SUPPLIER) additionally resolve or create that company via the same FMP/Yahoo pipeline as link-counterparty (`GetOrCreateCompanyAsync`) and create a reciprocal mirror row on it (`EnsureReciprocal`) — i.e. the relationship is saved bidirectionally. Returns `{ saved, links }` (rows saved, reciprocal links created). Responses: 200 OK; 400 (CompanyId missing); 404 (company not found) |

---

## /extraction/auto-extract/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | AutoExtract |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("auto-extract/{companyId:long}")]` |
| View | — (JSON source suggestions for the human to confirm) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required); node: string? (query string, optional — `REVENUE`\|`COST`\|`RISK`, default `REVENUE`); form: string? (query string, optional — SEC form type, e.g. `10-K`, forwarded to the sec2md markdown sidecar) |
| Notes | Mode B — AI (`IFilingExtractionService`) reads one SEC filing and proposes rows + per-field proof for the active node (revenue / cost / company-risk) for the human to confirm; persists nothing (the page fills the form and the existing save path freezes proof). Responses: 200 OK; 400 (missing accession/doc); 404 (no such company); 503 (Claude unreachable) |

---

## /extraction/scan-auto/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanAuto |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("scan-auto/{companyId:long}")]` |
| View | — (JSON `{ scanned, found }`) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required); node: string? (query string, optional — `REVENUE`\|`COST`\|`RISK`, default `REVENUE`); form: string? (query string, optional — SEC form type, e.g. `10-K`, forwarded to the sec2md markdown sidecar) |
| Notes | Mode B (auto) — triages every bold heading by title, scans the AI-chosen ones in parallel, and stashes the digest as the AI Chat's grounding; persists nothing to the DB. Replaces the hand-pick flow (`headings` + `scan-headings`). Responses: 200 OK; 400 (missing accession/doc); 404 (no such company); 503 (DeepSeek/SEC unreachable) |

---

## /extraction/scan-auto-async/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanAutoAsync |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("scan-auto-async/{companyId:long}")]` |
| View | — (JSON `{ jobId }`) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required); node: string? (query string, optional — `REVENUE`\|`COST`\|`RISK`, default `REVENUE`); form: string? (query string, optional — SEC form type, e.g. `10-K`, forwarded to the sec2md markdown sidecar); companyName: string? (query string, optional); filingLabel: string? (query string, optional) |
| Notes | Mode B (async) — same scan as `scan-auto`, but detached: registers a background `ScanJob` in the in-memory `ScanJobStore`, fires the scan + an auto AI-summary chat turn on a background task (own DI scope), and returns the `jobId` at once so the page doesn't block. The user can navigate away; the global notification widget polls `scan-jobs` for the result. Persists nothing to the DB. Responses: 200 OK (`{ jobId }`); 400 (missing accession/doc); 404 (no such company) |

---

## /extraction/scan-jobs

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanJobs |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("scan-jobs")]` |
| View | — (JSON array of jobs: `{ id, status, companyId, companyName, accession, doc, node, form, filingLabel, found, summary, error, replying }`) |
| Parameters | ids: string? (query string — comma-separated job ids the browser is tracking) |
| Notes | Returns the status/found-count/AI summary of the background jobs whose ids the browser holds (in localStorage). Resolves each id against both the `ScanJobStore` and the `RediscoverJobStore`, MERGING filing-scan and private-company re-discovery jobs into one list — each entry carries a `kind` (`"scan"` or `"rediscover"`) so the widget can tell a chat-capable scan from a fire-and-forget re-discovery. Unknown/dismissed ids are skipped. Backs the global bottom-right notification widget, which shows the summary and links back to `/extraction` to save the yielded objects |

---

## /extraction/scan-jobs/dismiss/{jobId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | DismissScanJob |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("scan-jobs/dismiss/{jobId}")]` |
| View | — (200 OK, no body) |
| Parameters | jobId: string (route) |
| Notes | Removes a job when the user dismisses it in the notification widget — clears the id from both the `ScanJobStore` and the `RediscoverJobStore`, so it dismisses filing-scan and re-discovery jobs alike |

---

## /extraction/scan-jobs/{jobId}/reply

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanJobReply |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("scan-jobs/{jobId}/reply")]` |
| View | — (200 OK, no body) |
| Parameters | jobId: string (route); JSON body — { messages:[{role,content}] } (the conversation so far) |
| Notes | Starts a DETACHED background chat reply for a finished scan job so the answer survives the user navigating away. The reply is generated on a background task (own DI scope) via `IExtractionChatService.StreamReplyAsync`, streamed into the job's `ReplyBuffer`/`ReplyThink`. Responses: 200 OK on start; 404 (unknown job); 400 (scan not finished); 409 (a reply is already in progress) |

---

## /extraction/scan-jobs/{jobId}/reply (GET)

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanJobReplyState |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("scan-jobs/{jobId}/reply")]` |
| View | — (JSON `{ replying, reply, think, error }`) |
| Parameters | jobId: string (route) |
| Notes | Polled by the notification widget to mirror the in-flight reply. Responses: 200 OK; 404 (unknown job) |

---

## /extraction/discover-related

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | DiscoverRelated |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("discover-related")]` |
| View | — (JSON array of CounterpartySuggestion) |
| Parameters | req: DiscoverCounterpartiesRequest (body) — { companyId, side (`CUSTOMER`=revenue \| `SUPPLIER`=cost), segments:[] (empty => the model identifies the company's segments itself) }; ct: CancellationToken |
| Notes | Web-search discovery via Perplexity `sonar-pro` — finds the named suppliers/customers behind a company's segments; persists nothing (the page lists them, the user confirms each via `link-counterparty`). See `docs/web_search.md`. Responses: 200 OK; 400 (missing companyId); 404 (no such company); 503 (web search unreachable) |

---

## /extraction/link-counterparty

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | LinkCounterparty |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("link-counterparty")]` |
| View | — (JSON `{ sourceId, counterpartyId, node }`) |
| Parameters | req: LinkCounterpartyRequest (body) — { companyId, name, side, classification, existingCompanyId?, countryCode?, sector?, ticker?, sourceUrl?, note? } |
| Notes | Confirms one discovered counterparty: reuse (fuzzy `MatchByName`) or create the company (FMP-by-ticker, else minimal), create the `RevenueSource`/`CostSource` row with `RelatedCompanyId`, and save the sonar citation as a `RELATED_COMPANY` `SourceFieldReview`. See `docs/web_search.md`. Responses: 200 OK; 400 (missing companyId/name or bad classification); 404 (no such company) |

---

## /graph

| Field | Value |
|---|---|
| Controller | GraphController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("graph")]` + `[Route("")]` |
| View | Views/Graph/Index.cshtml |
| Parameters | companyId: long? (query string, optional) |
| Notes | Renders network graph explorer with company picker; `?companyId=` selects starting center. ViewModel: `GraphIndexViewModel` (Models/ViewModels/GraphViewModels.cs) |

---

## /graph/data/company/{id}

| Field | Value |
|---|---|
| Controller | GraphController |
| Action | CompanyGraph |
| HTTP | GET |
| Route source | `[Route("graph")]` + `[Route("data/company/{id:long}")]` |
| View | — (JSON `GraphResponse` record with `GraphNode`, `GraphEdge` records) |
| Parameters | id: long (route, constrained) |
| Notes | Consumed by JS in Views/Graph/Index.cshtml; returns NotFound when company missing or soft-deleted |

---

## /impact

| Field | Value |
|---|---|
| Controller | ImpactController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("impact")]` + `[Route("", Name = "ImpactIndex")]` |
| View | Views/Impact/Index.cshtml |
| Parameters | — |
| Notes | The Impact Simulator page — an input-output cascade simulator. Follows the GraphController pattern: one MVC controller that both renders the interactive view and serves its JSON data action. ViewModel: none (returns `View()`) |

---

## /impact/solve

| Field | Value |
|---|---|
| Controller | ImpactController |
| Action | Solve |
| HTTP | POST |
| Route source | `[Route("impact")]` + `[Route("solve", Name = "ImpactSolve")]` |
| View | — (JSON: ranked sector impacts, per-sector companies, and a propagation trace) |
| Parameters | JSON body (`ImpactRequest`) — { kind, sector, magnitude } |
| Notes | Runs one input-output cascade (`EventImpactService.Solve`) and returns the ranked sector impacts, per-sector companies, and propagation trace. Responses: 200 OK; 400 (unknown `sector` or `kind`) |

---

## /impact/firms

| Field | Value |
|---|---|
| Controller | ImpactController |
| Action | Firms |
| HTTP | GET |
| Route source | `[Route("impact")]` + `[Route("firms", Name = "ImpactFirms")]` |
| View | — (JSON: companies eligible to originate a firm-level shock, each with id, name, sector, supplierCount, customerCount) |
| Parameters | — |
| Notes | The list of companies that can originate a firm-level shock — companies with ≥1 quantified counterparty link. Feeds the firm picker on the Impact Simulator page |

---

## /impact/solve-firm

| Field | Value |
|---|---|
| Controller | ImpactController |
| Action | SolveFirm |
| HTTP | POST |
| Route source | `[Route("impact")]` + `[Route("solve-firm", Name = "ImpactSolveFirm")]` |
| View | — (JSON: ranked firms and a round-by-round propagation trace) |
| Parameters | JSON body (`FirmImpactRequest`) — { kind, companyId, magnitude } |
| Notes | Runs one firm-level input-output cascade and returns the ranked firms plus a round-by-round trace. Responses: 200 OK; 400 (unknown `kind` or a `companyId` not in the firm graph) |

---

## api/search

| Field | Value |
|---|---|
| Controller | SearchController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("api/search")]` + `[HttpGet]` (`[AllowAnonymous]`) |
| View | — (JSON: grouped global-search results — Companies, Countries, Events) |
| Parameters | q: string? (query string) |
| Notes | Global search backing the nav-bar command bar and the home hero search input. Returns hits grouped by Kind (Companies / Countries / Events) from `ISearchService.Search`. JSON only, no Razor view |

---

## api/graph/company

| Field | Value |
|---|---|
| Controller | GraphController (Controllers/Api/) |
| Action | CompanyGraph |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` + `[HttpGet("company")]` |
| View | — (JSON `GraphResponse` record: `CenterId`, `CenterLabel`, `Nodes`, `Edges`) |
| Parameters | cik: string (query string, `[FromQuery]`) |
| Notes | Same hub-and-spoke graph as `/graph/data/company/{id}` but keyed by exact CIK; shares the `Company -> GraphResponse` AutoMapper converter. Returns 400 if `cik` blank, 404 if no company with that CIK |

---

## api/stock/refresh/{companyId}

| Field | Value |
|---|---|
| Controller | StockController (Controllers/Api/) |
| Action | Refresh |
| HTTP | POST |
| Route source | `[ApiController]` + `[Route("api/stock")]` + `[HttpPost("refresh/{companyId:long}")]` |
| View | — (JSON `CompanyDto`) |
| Parameters | companyId: long (route, constrained) |
| Notes | Fetches SEC EDGAR data for the company and (re)persists EDGAR-tagged RevenueSource/CostSource/Event rows. Responses: 200 OK; 404 (no such company); 409 (company has no CIK / non-filer); 422 (CIK not an SEC filer); 503 (SEC unreachable/timeout) |

---

## api/stock/resolve/{ticker}

| Field | Value |
|---|---|
| Controller | StockController (Controllers/Api/) |
| Action | Resolve |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/stock")]` + `[HttpGet("resolve/{ticker}")]` |
| View | — (JSON `{ ticker, cik }`) |
| Parameters | ticker: string (route) |
| Notes | Read-only ticker -> CIK lookup. Returns 200 OK or 404 if the ticker is unknown |

---

## api/stock/facts/{companyId}

| Field | Value |
|---|---|
| Controller | StockController (Controllers/Api/) |
| Action | Facts |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/stock")]` + `[HttpGet("facts/{companyId:long}")]` |
| View | — (raw SEC XBRL companyfacts JSON, passed through as application/json) |
| Parameters | companyId: long (route, constrained) |
| Notes | Read-only proxy of SEC `/api/xbrl/companyfacts/CIK{cik}.json`; no persistence. Responses: 200 OK; 404 (no such company); 409 (company has no CIK); 422 (CIK not an SEC filer); 503 (SEC unreachable) |

---

## api/stock/filings/{companyId}

| Field | Value |
|---|---|
| Controller | StockController (Controllers/Api/) |
| Action | Filings |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/stock")]` + `[HttpGet("filings/{companyId:long}")]` |
| View | — (JSON array of filing summaries: form, filingDate, reportDate, accessionNumber, primaryDocument, description, documentUrl) |
| Parameters | companyId: long (route, constrained) |
| Notes | Read-only; built from SEC submissions `filings.recent`; each row includes a ready-to-open sec.gov Archives documentUrl. Responses: 200 OK; 404; 409 (no CIK); 422 (no filings); 503 |

---

## api/stock/filing/{companyId}

| Field | Value |
|---|---|
| Controller | StockController (Controllers/Api/) |
| Action | Filing |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/stock")]` + `[HttpGet("filing/{companyId:long}")]` |
| View | — (the filing's primary document text, returned as text/plain) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required) |
| Notes | Read-only proxy of one filing document from sec.gov Archives so its text can be selected as proof in /extraction. Responses: 200 OK; 400 (missing accession/doc); 404 (no company / document not found); 409 (no CIK); 503 |

---

## api/companies/{id}/volume

| Field | Value |
|---|---|
| Controller | CompaniesController (Controllers/Api/) |
| Action | GetVolume |
| HTTP | GET |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` + `[HttpGet("{id:long}/volume")]` |
| View | — (JSON array of `{ weekStart, volume }`) |
| Parameters | id: long (route, constrained) |
| Auth | `[Authorize]` class-level (cookie or the MCP API-key scheme) |
| Notes | Read-only stored weekly trading-volume series (no SEC/Yahoo call — just persisted rows via `GetVolumeHistory`). Added for the read-only MCP server's `get_stock_volume` tool; the ingest path that fills it stays a separate Admin/Manager POST on the MVC `CompaniesController` (`/companies/{id}/ingest-volume`). Returns 404 when the company is not found |

---

## api/CompanyRisks

| Field | Value |
|---|---|
| Controller | CompanyRisksController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/CompanyRisks (`?q=` optional search), GET /api/CompanyRisks/{id:long}, POST /api/CompanyRisks, PUT /api/CompanyRisks/{id:long}, DELETE /api/CompanyRisks/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `CompanyRiskDto` / list) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `CompanyRiskRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD; Delete is a soft-delete (`_repo.SoftDelete`) |

---

## api/CompanyFinancials

| Field | Value |
|---|---|
| Controller | CompanyFinancialsController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/CompanyFinancials (`?q=` optional search), GET /api/CompanyFinancials/{id:long}, POST /api/CompanyFinancials, PUT /api/CompanyFinancials/{id:long}, DELETE /api/CompanyFinancials/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `CompanyFinancialDto` / list) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `CompanyFinancialRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD; Delete is a soft-delete (`_repo.SoftDelete`) |

---

## api/Filings

| Field | Value |
|---|---|
| Controller | FilingsController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/Filings (`?q=` optional search), GET /api/Filings/{id:long}, POST /api/Filings, PUT /api/Filings/{id:long}, DELETE /api/Filings/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `FilingDto` / list) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `FilingRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD, except Create calls `_repo.Upsert(companyId, accessionNumber, form, filingDate, primaryDocUrl)` instead of a plain insert — AccessionNumber has a unique index, so an existing row for the same accession is revived/refreshed rather than colliding. Delete is a soft-delete (`_repo.SoftDelete`) |

---

## api/SourceFieldReviews

| Field | Value |
|---|---|
| Controller | SourceFieldReviewsController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/SourceFieldReviews (`?q=` optional search), GET /api/SourceFieldReviews/{id:long}, POST /api/SourceFieldReviews, PUT /api/SourceFieldReviews/{id:long}, DELETE /api/SourceFieldReviews/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `SourceFieldReviewDto` / list) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `SourceFieldReviewRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD; Delete is a soft-delete (`_repo.SoftDelete`) |

---

## api/Scenarios

| Field | Value |
|---|---|
| Controller | ScenariosController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/Scenarios (`?q=` optional search), GET /api/Scenarios/{id:long}, POST /api/Scenarios, PUT /api/Scenarios/{id:long}, DELETE /api/Scenarios/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `ScenarioDto` / list — includes nested `Shocks: List<ScenarioShockDto>`) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `ScenarioRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD; Delete is a soft-delete (`_repo.SoftDelete`). `ScenarioDto` carries the scenario's `ScenarioShock` rows nested under `Shocks` |

---

## api/ScenarioShocks

| Field | Value |
|---|---|
| Controller | ScenarioShocksController (Controllers/Api/) |
| Action | GetAll / GetById / Create / Update / Delete |
| HTTP | GET /api/ScenarioShocks (`?q=` optional search), GET /api/ScenarioShocks/{id:long}, POST /api/ScenarioShocks, PUT /api/ScenarioShocks/{id:long}, DELETE /api/ScenarioShocks/{id:long} |
| Route source | `[ApiController]` + `[Route("api/[controller]")]` |
| View | — (JSON `ScenarioShockDto` / list) |
| Parameters | q: string? (query, GetAll); id: long (route, constrained); body: `ScenarioShockRequestDto` (Create/Update) |
| Auth | `[Authorize]` class-level (GET); `[Authorize(Roles = "Admin,Manager")]` on Create/Update/Delete |
| Notes | Standard CRUD, except Delete is a HARD delete (`db.ScenarioShocks.Remove` via `_repo.Delete`) — ScenarioShock has no `DeletedAt` column |

---

## /Account/Profile

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | Profile |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`) |
| View | Views/Account/Profile.cshtml |
| Parameters | — |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | The signed-in user's own account page — profile details plus a Dropzone profile-picture upload (single-file model). Backed by `AppUser` |

---

## /Account/CurrentPicture

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | CurrentPicture |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`) |
| View | — (JSON `{ hasPicture }` or `{ hasPicture, path, name, contentType, size, uploadedAt }`) |
| Parameters | — |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | AJAX metadata for the current profile picture, consumed by the Profile page's Dropzone to render the existing file. Returns `{ hasPicture: false }` (not 404) when there's no picture |

---

## /Account/UploadProfilePicture

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | UploadProfilePicture |
| HTTP | POST |
| Route source | Convention (`/{controller}/{action}/{id?}`) (ValidateAntiForgeryToken) |
| View | — (JSON `{ path, name, size }`) |
| Parameters | file: IFormFile (Dropzone field name "file") |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | Dropzone upload — saves to `wwwroot/uploads/profiles/{userId}/{guid}{ext}`, records metadata on `AppUser`, and replaces any existing picture. Validates JPEG/PNG/GIF/WebP only and a 5 MB limit. Responses: 200 OK; 400 (no file / over limit / disallowed type) |

---

## /Account/DeleteProfilePicture

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | DeleteProfilePicture |
| HTTP | POST |
| Route source | Convention (`/{controller}/{action}/{id?}`) (ValidateAntiForgeryToken) |
| View | — (200 OK, no body) |
| Parameters | — |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | Deletes the current profile picture (physical file + `AppUser` metadata) |

---

## /Account/ApiKeys

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | ApiKeys |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`) |
| View | Views/Account/ApiKeys.cshtml |
| Parameters | — |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | Bring-your-own API keys management page — the signed-in user's own DeepSeek / FMP / Perplexity keys, stored encrypted (Data Protection) in `UserApiKeys`. Shows only "set · ••••last4" status, never the raw key. Linked from the user dropdown nav in Views/Shared/_LoginPartial.cshtml. ViewModel: `ApiKeysViewModel` |

---

## /Account/SaveApiKeys

| Field | Value |
|---|---|
| Controller | AccountController |
| Action | SaveApiKeys |
| HTTP | POST |
| Route source | Convention (`/{controller}/{action}/{id?}`) (ValidateAntiForgeryToken) |
| View | — (redirects to ApiKeys) |
| Parameters | deepSeekKey: string?; fmpKey: string?; perplexityKey: string? (form); clearDeepSeek/clearFmp/clearPerplexity: bool (form) |
| Auth | `[Authorize]` (any authenticated user) |
| Notes | Saves the user's encrypted API keys to `UserApiKeys`. Per key: a "clear" tick removes it; a non-blank input sets it (encrypted); blank leaves it as-is (so one key can be updated without re-typing the others). Redirects back to ApiKeys |

---

## /Admin

| Field | Value |
|---|---|
| Controller | AdminController |
| Action | Index |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`, defaults action Index) |
| View | Views/Admin/Index.cshtml |
| Parameters | — |
| Auth | `[Authorize(Roles = "Admin")]` |
| Notes | Admin-only user list — each row shows email, username, profile picture, and assigned roles (`AdminUserRow`) |

---

## /Admin/EditRoles

| Field | Value |
|---|---|
| Controller | AdminController |
| Action | EditRoles |
| HTTP | GET, POST |
| Route source | Convention (`/{controller}/{action}/{id?}`) (POST is ValidateAntiForgeryToken) |
| View | Views/Admin/EditRoles.cshtml |
| Parameters | GET: userId: string (query); POST: `EditRolesViewModel` (form) |
| Auth | `[Authorize(Roles = "Admin")]` |
| Notes | Role-assignment form for one user — GET renders a checkbox per role; POST diffs current vs selected and only adds/removes the roles that changed, then redirects to Index. 404 when the user id is unknown |

---

## /Admin/Delete

| Field | Value |
|---|---|
| Controller | AdminController |
| Action | Delete |
| HTTP | POST |
| Route source | Convention (`/{controller}/{action}/{id?}`) (ValidateAntiForgeryToken) |
| View | — (redirects to Index) |
| Parameters | userId: string (query/form) |
| Auth | `[Authorize(Roles = "Admin")]` |
| Notes | Deletes a user. 404 when the user id is unknown; 400 when an admin tries to delete their own account |

---

## /Identity/Account/* (ASP.NET Core Identity default UI)

| Field | Value |
|---|---|
| Controller | Identity default UI Razor Pages (area `Identity`) |
| Action | Register, Login, Logout, ExternalLogin (Google), etc. |
| HTTP | GET, POST |
| Route source | Razor Pages, area `Identity` (`/Identity/Account/{page}`) |
| View | Scaffolded Identity Razor Pages (default UI) |
| Parameters | per-page (form fields) |
| Auth | Anonymous (Register/Login/ExternalLogin); authenticated (Logout/Manage) |
| Notes | The built-in Identity UI — `/Identity/Account/Register`, `/Identity/Account/Login`, `/Identity/Account/Logout`, `/Identity/Account/ExternalLogin` (Google sign-in), and the other default account pages |

---

## Authorization model

ASP.NET Core Identity now gates the existing controllers as follows:

- **MVC entity controllers** (Countries, Companies, Events, TradeBlocs, CountryDetails, CountryAdvantages, CountryChallenges, GdpSnapshots, RevenueSources, CostSources, Indices): read GETs (`Index`/`Search`/`Details`/`Lookup`/`ValidateCountry` and the like) are `[AllowAnonymous]`; `Create`/`Edit`/`Delete` (and the other mutating actions) require `[Authorize(Roles = "Admin,Manager")]`.
- **API controllers** (Controllers/Api — GraphController, StockController, CompanyRisks, CompanyFinancials, Filings, SourceFieldReviews, Scenarios, ScenarioShocks): `GET` requires `[Authorize]` (any authenticated user); `POST`/`PUT`/`DELETE` require `[Authorize(Roles = "Admin,Manager")]`.
- **Public** (no auth): Home, Ticker, Graph, Impact.
- **ExtractionController**: `[Authorize(Roles = "Admin,Manager")]` for the whole controller.
- **AccountController**: `[Authorize]` (any authenticated user).
- **AdminController**: `[Authorize(Roles = "Admin")]`.

---

## /Home/Error

| Field | Value |
|---|---|
| Controller | HomeController |
| Action | Error |
| HTTP | GET |
| Route source | Convention (`/{controller}/{action}/{id?}`) |
| View | Views/Home/Error.cshtml |
| Parameters | — |
| Notes | Not user-facing — hit automatically by `UseExceptionHandler("/Home/Error")` in production |

---

## Navigation (Views/Shared/_Layout.cshtml)

Top nav links: Countries, Companies, Indices, Events, Trade Blocs, Graph, plus a "More" dropdown containing: Country Details, Country Advantages, Country Challenges, GDP Snapshots, Revenue Sources, Cost Sources, Extraction.
