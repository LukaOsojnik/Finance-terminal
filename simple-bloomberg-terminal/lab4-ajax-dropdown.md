# Padajući izbornik s AJAX autocomplete pretragom

Dropdown (combobox) je nezaobilazna kontrola za unos polja koje je strani ključ (1-N veza).

---

## Vrste dropdown kontrola

Odabir pristupa ovisi o količini podataka i načinu korištenja.

### 1. Statički dropdown

Za mali broj opcija (< 20).

Primjeri: status narudžbe, tip korisnika, kategorija s malo vrijednosti.

Server kod otvaranja forme šalje **sve opcije**, view ih samo iscrtava.

### 2. Autocomplete dropdown s dohvatom sa servera

Za veći broj opcija — korisnici, gradovi, adrese, proizvodi, veliki šifrarnici.

Korisnik počne pisati → JavaScript šalje **AJAX zahtjev** serveru → server vraća samo opcije koje odgovaraju upitu.

**Ovo je obvezan dio vježbe** — demonstrira AJAX pozive, custom kontrolu i server-side pretragu.

### 3. Hibridni dropdown

Broj opcija nije jako velik, ali korisno je omogućiti pretragu.

Sve opcije se učitaju unaprijed, **filtriranje se radi na klijentu** (bez dodatnog odlaska na server).

---

## Autocomplete u Create i Edit formama

### Create forma

Nije potrebno odmah slati sve opcije. Kontrola zna endpoint koji će pozvati kad korisnik počne pisati.

Flow:
1. Korisnik upiše `Zag`
2. JavaScript → AJAX na endpoint za pretragu gradova
3. Server vraća prvih N rezultata
4. Korisnik odabere jedan rezultat
5. U **hidden input** sprema se ID odabrane opcije

### Edit forma

Mora prikazati **već odabranu vrijednost** (tekst, ne samo ID).

Server šalje:
- **ID** odabrane opcije (npr. `CityId = 5`)
- **Tekst** koji se prikazuje korisniku (npr. `Zagreb`)

---

## Statički dropdown — implementacija

### Model

```csharp
public class Quiz
{
    public int Id { get; set; }
    public DateTime DateCreated { get; set; }
    public int? CategoryId { get; set; }
    public QuizCategory Category { get; set; }
}
```

### Controller — punjenje ViewBag-a

```csharp
var selectItems = new List<SelectListItem>();

// Opcionalno prazno polje
var listItem = new SelectListItem();
listItem.Text = "- odaberite -";
listItem.Value = "";
selectItems.Add(listItem);

foreach (var category in _db.QuizCategories)
{
    listItem = new SelectListItem();
    listItem.Text = category.Name;        // prikazuje se korisniku
    listItem.Value = category.Id.ToString();  // vrijednost koja se sprema
    selectItems.Add(listItem);
}

ViewBag.PossibleCategories = selectItems;
```

**Ovaj kod mora se izvršiti na 4 mjesta:**
- `GET Create`
- `POST Create` (ako validacija padne, treba ponovno prikazati formu s dropdown opcijama)
- `GET Edit`
- `POST Edit` (ako validacija padne)

Zato je preporučljivo izdvojiti ga u **posebnu funkciju**.

### View

```html
<div class="form-group">
    <label class="control-label">Category</label>
    <select asp-for="CategoryID" asp-items="ViewBag.PossibleCategories" class="form-control"></select>
</div>
```

---

## Performanse autocomplete pretrage

| Količina zapisa | Pristup |
|---|---|
| Do nekoliko tisuća | Jednostavna pretraga — filtriranje + `Take(10-20)` |
| Do ~10.000 | Uz dobar upit i indeks uglavnom prihvatljivo |
| Iznad 10.000 | Indeksi, `StartsWith`, ili full-text search |

Full-text search ima cijenu — dodatni indeks može biti velik i složen za održavanje. Ne koristiti automatski za svaki dropdown.

---

## Ponovno korištenje dropdown kontrole

Cilj: **ne implementirati svaki dropdown ispočetka.** Napraviti jednu dobru kontrolu i ponovno je koristiti.

Preporučeni pristup:
1. Napraviti **partial view** za autocomplete kontrolu
2. Definirati jasan format podataka koji kontrola očekuje
3. Definirati **endpoint** koji vraća rezultate pretrage
4. Testirati kontrolu na **jednom entitetu**
5. Nakon toga istu kontrolu koristiti na drugim formama

Kod AI alata: prvo ručno/iterativno dovesti jednu formu do dobrog stanja, zatim AI-u dati uputu da ostale forme napravi po istom obrascu.
