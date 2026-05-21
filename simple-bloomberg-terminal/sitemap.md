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

---

## /revenue-sources/{id}/edit

| Field | Value |
|---|---|
| Controller | RevenueSourcesController |
| Action | Edit |
| HTTP | GET, POST |
| Route source | `[Route("revenue-sources")]` + `[Route("{id:long}/edit")]` |
| View | Views/RevenueSources/Edit.cshtml |
| Parameters | id: long (route, constrained); form fields (POST) |

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

Top nav links: Countries, Companies, Events, Trade Blocs, Graph, plus a "More" dropdown containing: Country Details, Country Advantages, Country Challenges, GDP Snapshots, Revenue Sources, Cost Sources.
