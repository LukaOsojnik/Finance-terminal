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

---

## /companies/{id}/delete

| Field | Value |
|---|---|
| Controller | CompaniesController |
| Action | Delete |
| HTTP | POST |
| Route source | `[Route("companies")]` + `[Route("{id:long}/delete")]` |
| View | — (soft-delete, redirects) |
| Parameters | id: long (route, constrained) |

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

## /extraction

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("")]` |
| View | Views/Extraction/Index.cshtml |
| Parameters | companyId: long? (query string, optional) |
| Notes | Phase-1 split-screen revenue extraction UI — company autocomplete picker; left pane = RevenueSource cells, right pane = JSON from `POST /api/stock/refresh/{companyId}`; "Use as reference" writes per-cell SourceFieldReview rows |

---

## /extraction/references/{revenueSourceId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | References |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("references/{revenueSourceId:long}")]` |
| View | — (JSON array of per-field references: field, snapshot, pointer, endpoint, mark, rationale, filing) |
| Parameters | revenueSourceId: long (route, constrained) |
| Notes | Existing references for a source row, so the extraction page can show each cell's pointer on load. Returns 404 when the source row id is not found |

---

## /extraction/reference

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Reference |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("reference")]` |
| View | — (JSON `ReferenceResult` record: RevenueSourceId, ReviewId, Field) |
| Parameters | JSON body (ReferenceRequest) |
| Notes | Ensures the RevenueSource row exists (create if new, DataSource=MANUAL) then upserts one SourceFieldReview per (RevenueSourceId, Field) with Mark=null; re-referencing resets the verdict. Responses: 200 OK; 400 (missing companyId/name/snapshot); 404 (source row id not found). Superseded in the UI by `POST /extraction/save` (endpoint still present but no longer called) |

---

## /extraction/save

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Save |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("save")]` |
| View | — (JSON `{ revenueSourceId, proofs }`) |
| Parameters | JSON body (SaveRequest): companyId, revenueSourceId?, sourceType, name, value, percentage, relatedCompanyId, proofs:[{field, endpoint, referencePointer, referenceSnapshot, referencedValue, filingAccessionNumber, filingForm, filingDate, filingUrl}] |
| Notes | Current UI save path — saves the whole extraction form in one request: upserts the RevenueSource row from the field values, then upserts one SourceFieldReview per entry in Proofs. Responses: 200 OK; 400 (bad companyId / missing name / invalid sourceType); 404 (revenueSourceId given but row not found) |

---

## /extraction/review/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Review |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("review/{companyId:long}")]` |
| View | — (JSON tally: reviewed, passed, failed, skipped) |
| Parameters | companyId: long (route, constrained) |
| Notes | Mode A — runs the phase-2 AI reviewer (`IReviewService`) over the company's unreviewed SourceFieldReview cells (human-entered value + proof) and returns the pass/fail tally so the page can refresh its marks. Responses: 200 OK; 404 (no such company); 503 (Claude unreachable) |

---

## /extraction/auto-extract/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | AutoExtract |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("auto-extract/{companyId:long}")]` |
| View | — (JSON revenue-source suggestions for the human to confirm) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required) |
| Notes | Mode B — AI (`IFilingExtractionService`) reads one SEC filing and proposes revenue rows + per-field proof for the human to confirm; persists nothing (the page fills the form and the existing save path freezes proof). Responses: 200 OK; 400 (missing accession/doc); 404 (no such company); 503 (Claude unreachable) |

---

## /extraction/chat

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Chat |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("chat")]` |
| View | — (streaming `application/x-ndjson` body of `{"t":"reasoning"\|"text"\|"error","c":"..."}` lines, not a normal JSON/view response) |
| Parameters | req: ChatRequest (body) — { companyId, accession, doc, messages:[{role,content}] }; ct: CancellationToken |
| Notes | Mode B — streams a conversational extraction grounded on one open SEC filing; persists nothing (`​```save```​` blocks in the reply pre-fill the extraction form). Responses: 200 OK (NDJSON stream); 404 (no such company) |

---

## /extraction/headings/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | Headings |
| HTTP | GET |
| Route source | `[Route("extraction")]` + `[Route("headings/{companyId:long}")]` |
| View | — (JSON list of sub-headings: `[{ id, title, section, chars }]`) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required) |
| Notes | Mode B — returns the bold sub-headings found inside Items 7/8/1A of the filing so the "Pick Sections" UI can let the user choose which sections to scan. Responses: 200 OK; 400 (missing accession/doc); 404 (no such company); 503 (SEC unreachable) |

---

## /extraction/scan-headings/{companyId}

| Field | Value |
|---|---|
| Controller | ExtractionController |
| Action | ScanHeadings |
| HTTP | POST |
| Route source | `[Route("extraction")]` + `[Route("scan-headings/{companyId:long}")]` |
| View | — (JSON `{ findings }` — candidate count) |
| Parameters | companyId: long (route, constrained); accession: string (query, required); doc: string (query, required); body: int[] (picked heading ids) |
| Notes | Mode B — spawns one parallel worker per picked heading, scans only those paragraphs, and stores the result as the AI Chat's grounding digest; persists nothing to the DB. Responses: 200 OK; 400 (missing accession/doc or empty selection); 404 (no such company); 503 (DeepSeek/SEC unreachable) |

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

Top nav links: Countries, Companies, Events, Trade Blocs, Graph, plus a "More" dropdown containing: Country Details, Country Advantages, Country Challenges, GDP Snapshots, Revenue Sources, Cost Sources, Extraction.
