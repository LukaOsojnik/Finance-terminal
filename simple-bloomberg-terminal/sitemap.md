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

## /events/feed

| Field | Value |
|---|---|
| Controller | EventsController |
| Action | Index |
| HTTP | GET |
| Route source | `[Route("events")]` + `[Route("feed")]` |
| View | Views/Events/Index.cshtml |
| Parameters | — |

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
