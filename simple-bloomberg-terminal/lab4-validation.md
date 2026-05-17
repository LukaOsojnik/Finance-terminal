# Implementacija validacije (client side + server side)

Validacija je jedan od bitnijih segmenata svake aplikacije. Osnovni koncepti:
- Onemogućiti spremanje nekonzistentnih podataka
- Prikazati adekvatnu poruku korisniku u slučaju pogreške

ASP.NET MVC nudi gotova rješenja, ali i naprednu prilagodbu na svim razinama.

---

## Tri razine validacije

### 1. Client side validacija

**Svrha:** bolji UX. Korisnik odmah vidi grešku, bez čekanja servera.

Primjeri:
- Obvezno polje nije popunjeno
- Email nije u ispravnom formatu
- Broj nije u dopuštenom rasponu
- Datum nije u očekivanom formatu

**Ne smije joj se vjerovati!** Korisnik može zaobići validaciju slanjem vlastitog HTTP zahtjeva ili izmjenom DOM-a.

### 2. Server side validacija

**Najvažnija za sigurnost.** Server mora provjeriti sve, čak i ako ista provjera postoji na klijentu.

Server provjerava:
- Jesu li podaci ispravni
- Ima li korisnik pravo napraviti akciju
- Smije li korisnik vidjeti/mijenjati konkretan zapis
- Krši li zahtjev poslovna pravila

### 3. Validacija u bazi podataka

**Zadnja linija obrane** za konzistentnost podataka:
- Obvezni stupci ne smiju biti `null`
- Strani ključevi moraju pokazivati na postojeće zapise
- Jedinstvena polja (UNIQUE constraint)
- Tipovi podataka odgovaraju definiciji stupaca

> **EF Core i SQL injection:** Standardni LINQ upiti su parametrizirani — SQL injection u pravilu nije problem. I dalje izbjegavati ručno spajanje SQL stringova iz korisničkog unosa.

---

## Anotacije modela

Osnovna metoda validacije u ASP.NET MVC.

Koraci:
1. Anotirati željena svojstva u modelu
2. U viewu dodati `asp-validation-for` za prikaz poruke
3. U controlleru provjeriti `ModelState.IsValid`

---

### `[Required]` — obvezno polje

Potrebno: referenca na `System.ComponentModel.DataAnnotations` u model projektu (.NET Core je uključuje automatski).

```csharp
using System.ComponentModel.DataAnnotations;

public class Quiz
{
    public int Id { get; set; }

    [Required]
    public string Title { get; set; }

    public string Keywords { get; set; }
    public string Author { get; set; }
    public DateTime DateCreated { get; set; }
    public QuizCategory Category { get; set; }
    public List<Question> Questions { get; set; }
}
```

> **.NET 6+ napomena:** `string` svojstva bez `?` su implicitno not-null / `[Required]`. Za nullable: `string? LastName`.

### View — prikaz validacijske poruke

```html
<div class="form-group">
    <label asp-for="Email" class="control-label"></label>
    <input asp-for="Email" class="form-control" />
    <span asp-validation-for="Email" class="text-danger"></span>
</div>
```

### Layout — učitavanje validacijskih skripti

```html
<script src="~/lib/jquery/dist/jquery.min.js"></script>
<script src="~/lib/bootstrap/dist/js/bootstrap.bundle.min.js"></script>
<partial name="_ValidationScriptsPartial" />
<script src="~/js/site.js" asp-append-version="true"></script>
@await RenderSectionAsync("Scripts", required: false)
```

`_ValidationScriptsPartial` je partial view iz predloška projekta s osnovnim validacijskim skriptama.

### Controller — server-side provjera

```csharp
[HttpPost]
public IActionResult Create(Quiz model)
{
    if (ModelState.IsValid)
    {
        _dbContext.Quizes.Add(model);
        _dbContext.SaveChanges();
        return RedirectToAction(nameof(Index));
    }

    return View(model);  // vraća formu s greškama
}

[HttpPost, ActionName("Edit")]
public async Task<IActionResult> EditPost(int id)
{
    var quiz = _dbContext.Quizes.FirstOrDefault(p => p.ID == id);
    var ok = await this.TryUpdateModelAsync(quiz);

    if (ok)
    {
        _dbContext.SaveChanges();
        return RedirectToAction(nameof(Index));
    }

    return View(quiz);
}
```

> `TryUpdateModelAsync` vraća `true`/`false` — automatski provjerava validaciju i puni `ModelState`.

### Nakon neuspješne validacije

Kad se forma ponovno prikazuje (return View), potrebno je **ponovno popuniti** sve dropdown podatke (`ViewBag` kolekcije). Zato se kod za punjenje dropdowna izdvaja u posebnu funkciju.

---

## Ostale ugrađene validacije

Više validacija može se staviti na isto polje:

```csharp
public class Question
{
    [Required]
    [Range(1, 50)]
    public int Points { get; set; }

    [Required]
    [StringLength(2000, MinimumLength = 5)]
    public string QuestionText { get; set; }
}
```

### Prilagođene poruke

```csharp
[Required]
[Range(1, 50, ErrorMessage = "Broj bodova mora biti između 1 i 50.")]
public int Points { get; set; }
```

---

## Pregled ugrađenih anotacija

| Atribut | Opis |
|---|---|
| `[Required]` | Polje ne smije biti prazno/null |
| `[Range(min, max)]` | Brojčani raspon |
| `[StringLength(max, MinimumLength = min)]` | Duljina stringa |
| `[EmailAddress]` | Format email adrese |
| `[RegularExpression("pattern")]` | Regex validacija |
| `[Compare("OtherProperty")]` | Usporedba s drugim poljem |

Više: [Microsoft docs — Model validation](https://docs.microsoft.com/en-us/aspnet/core/mvc/models/validation?view=aspnetcore-6.0)
