# Napredno korištenje JavaScripta

JavaScript je skriptni jezik koji se izvodi u pregledniku. Najčešće se koristi s **jQuery** bibliotekom za efikasnu manipulaciju DOM-om i podacima.

---

## Osnove: `script` tag

```html
<li class="three" onclick="onLiElementClick()">
    ...
</li>

<script type="text/javascript">
    function onLiElementClick() {
        alert('<li> element clicked!');
    }

    alert('Kod koji se odmah izvodi!');
</script>
```

Tri načina izvođenja:
- `onclick="funkcija()"` — kod na korisničku akciju
- `function ime() { }` — definicija funkcije, izvodi se tek na poziv
- `alert('...')` — kod koji se izvodi odmah pri iscrtavanju stranice

---

## Redoslijed izvođenja i organizacija view datoteka

Browser izvodi kod **odozgo prema dolje** — važno je gdje stoji `script` tag.

Raspored elemenata u MVC-u:
- **Header** — u `_Layout.cshtml`
- **Footer** — u `_Layout.cshtml`
- **Sadržaj** — u specifičnom viewu (`Client/Index.cshtml`)

### Struktura `_Layout.cshtml` (redom)

1. **`<head>`** — meta podaci, `title` iz `ViewData["Title"]`, CSS datoteke
2. **`<environment>` tagovi** — različiti CSS/JS za dev i produkciju
3. **Navigacija** (navbar)
4. **Cookie consent partial**
5. **`@RenderBody()`** — **jedini obvezan dio** layouta; ovdje se ubacuje sadržaj trenutnog viewa
6. **Footer**
7. **JS biblioteke na kraju** (jQuery, Bootstrap, site.js)
8. **`@RenderSection("Scripts", required: false)`** — mjesto za view-specific JS

```html
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <title>@ViewData["Title"] - Vjezba.Web</title>

    <environment include="Development">
        <link rel="stylesheet" href="~/css/site.css" />
    </environment>
    <environment exclude="Development">
        <link rel="stylesheet" href="~/css/site.min.css" />
    </environment>
</head>
<body>
    <nav class="navbar navbar-inverse navbar-fixed-top">...</nav>
    <partial name="_CookieConsentPartial" />

    <div class="container body-content">
        @RenderBody()
        <hr />
        <footer><p>&copy; 2019 - Vjezba.Web</p></footer>
    </div>

    <environment include="Development">
        <script src="https://ajax.aspnetcdn.com/ajax/jquery/jquery-3.3.1.min.js"></script>
        <script src="~/js/site.js" asp-append-version="true"></script>
    </environment>
    <environment exclude="Development">
        <script src="https://ajax.aspnetcdn.com/ajax/jquery/jquery-3.3.1.min.js"></script>
        <script src="~/js/site.min.js" asp-append-version="true"></script>
    </environment>

    @RenderSection("Scripts", required: false)
</body>
</html>
```

### View-specific JS

```html
@model List<Client>

<h2>Popis klijenata</h2>
...

@section Scripts {
    <script type="text/javascript">
        alert('Poziv iz view-a Client/Index.cshtml');
    </script>
}
```

## Pravila

- **CSS** na početku (head) — korisnik što prije vidi stiliziran sadržaj
- **JavaScript** na kraju bodyja — funkcionalnost može pričekati
- **`@section Scripts`** — za JS koji ovisi o jQueryju (jer se jQuery učitava prije sekcije)
- **`@section` ne radi u PartialView-u** — samo u "pravim" viewovima
- Ako se skripta napiše prije jQueryja → **neće raditi**

---

## jQuery

Četiri osnovna koncepta:
- **jQuery selectors** — određivanje na koje elemente se primjenjuje manipulacija
- **jQuery AJAX** — jednostavni AJAX pozivi
- **jQuery events** — pridruživanje koda uz događaje na elementima
- **jQuery data** — vezivanje podataka uz HTML elemente

### DOM ready

Većina koda se izvršava kad je DOM učitan ili na korisničku akciju.

```javascript
// Puna sintaksa
$(document).ready(function () {
    // DOM je učitan
});

// Kraća sintaksa (ekvivalent)
$(function () {
    // DOM je učitan
});
```

---

## jQuery selectors — primjeri

### Primjer 1: Selektiranje po ID-u

Nakon DOM loada, postavi pozadinu tablice na žuto.

```html
<table id="tbl-clients" class="table table-condensed">
    ...
</table>

@section scripts {
    <script type="text/javascript">
        $(function () {
            $("#tbl-clients").css("background", "yellow");
        });
    </script>
}
```

`#` označava selektor po `id` atributu.

### Primjer 2: Klik na redak — promijeni tekst

```html
<tbody>
    @foreach (var client in Model)
    {
        <tr onclick="changeText(this)">
            ...
        </tr>
    }
</tbody>

@section scripts {
    <script type="text/javascript">
        function changeText(tr) {
            $(tr).find("td").text("-");
        }
    </script>
}
```

`$(tr).find("td")` — nađi sve `td` unutar kliknutog retka.

### Primjer 3: Dupliciranje retka na hover

```html
<tr onclick="changeText(this)" onmouseover="duplicateRow(this)">
    ...
</tr>

@section scripts {
    <script type="text/javascript">
        function duplicateRow(tr) {
            var dupl = $(tr).clone();
            $("table.table tbody").append(dupl);
        }
    </script>
}
```

`$(tr).clone()` — kloniraj element.
`$("table.table tbody").append(dupl)` — dodaj klon na kraj tbodyja.

---

## jQuery selektori — pregled

| Selektor | Značenje |
|---|---|
| `$("#id")` | Po ID-u |
| `$(".class")` | Po klasi |
| `$("tag")` | Po HTML tagu |
| `$(this)` | Trenutni element |
| `$(el).find("td")` | Traženje djece |
| `$(el).clone()` | Kloniranje elementa |
| `$("parent child")` | Hijerarhijski selektor |

Dokumentacija: [api.jquery.com/category/selectors](http://api.jquery.com/category/selectors/)
