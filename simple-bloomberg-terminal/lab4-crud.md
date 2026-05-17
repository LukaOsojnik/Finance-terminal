# CRUD podrška za sve entitete

## Delete

Najjednostavnija operacija — prima `ID`, dohvaća entitet i poziva `Remove` nad `DbSet` kolekcijom.

Kod brisanja treba razmisliti o relacijama. Tri pristupa:

| Strategija | Ponašanje |
|---|---|
| **Cascade delete** | Brišu se i povezani zapisi |
| **Set null** | Strani ključ se postavlja na `null` |
| **Zabrana brisanja** | Operacija ne uspijeva ako postoje povezani podaci |

### Soft delete

U stvarnim aplikacijama često je bolje ne brisati zapis fizički. Umjesto toga koristi se **soft delete**.

Entitet dobije polje `DeletedAt` (`DateTime?`):
- `null` — zapis je aktivan
- popunjeno — zapis je "obrisan"

Pravila:
- Kod brisanja **ne pozivati `Remove`**, nego postaviti `DeletedAt = DateTime.UtcNow`
- Kod dohvaćanja liste **uvijek filtrirati** `DeletedAt == null`

Prednost: lakši oporavak podataka.
Nedostatak: svaka lista, pretraga i dohvat moraju paziti da ne prikazuju obrisane zapise.

---

## Create

Controller prima podatke za novi zapis — najčešće s forme, ali mogu doći i iz druge akcije (npr. gumb koji kreira zapis s unaprijed zadanim vrijednostima).

Koristiti poseban model forme, ne direktno entitet.

---

## Edit

Složeniji od Create — radi se s postojećim zapisom.

---

## Entiteti vs modeli forme

**Pravilo:** Ne bindati podatke s forme direktno na entitet. Koristiti posebne klase:

- `QuizCreateModel` / `QuizEditModel`
- `ClientCreateModel` / `ClientEditModel`

Razlozi:
- Entitet može imati polja koja se ne smiju slati na frontend (`PasswordHash`, `PasswordSalt`, `IsLocked`, `LockoutEndTime`, `LastLoginAt`…)
- Entitet može imati interna polja koja korisnik ne uređuje
- Forma može imati polja koja ne odgovaraju 1:1 strukturi baze
- Sigurnije je eksplicitno kontrolirati što korisnik može mijenjati

Preporučeni flow za Edit:
1. Dohvatiti entitet iz baze
2. Mapirati potrebna polja u model forme
3. Prikazati model forme korisniku
4. Kod spremanja ponovno dohvatiti entitet
5. Mapirati samo dopuštena polja iz modela forme natrag u entitet
6. Spremiti

AI alati mogu brzo generirati mapiranje — i dalje treba provjeriti rezultat.

---

## `TryUpdateModel`

### Problem direktnog bindanja u Edit

```csharp
// GET — prikaz forme
public ActionResult Edit(int id)
{
    var client = _db.Clients.Find(id);  // dohvat iz baze
    return View(client);                 // prikaz forme
}

// POST — spremanje
[HttpPost]
public ActionResult Edit(int id, Client model)
{
    _db.SaveChanges();  // Što s poljima koja nisu na formi? Gube se!
}
```

Ako entitet ima polja koja nisu na formi, direktnim bindanjem (`Client model`) ta polja postaju `null` / default.

### Siguran pristup s `TryUpdateModel`

1. `GET Edit` — dohvat iz baze, prikaz forme
2. `POST Edit` — **ponovno dohvatiti** aktualni entitet iz baze, pozvati `TryUpdateModel` da prepiše samo podatke s forme, spremiti

```csharp
[HttpPost]
[ActionName("Edit")]
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

`TryUpdateModel` mapira podatke s forme na postojeći objekt **bez prepisivanja polja koja nisu na formi**.

Najoptimalnije rješenje: koristiti potpuno drugi objekt (`ClientViewModel`) i ručno premapirati polja. Ali za većinu entiteta dovoljan je `TryUpdateModel`.

---

## Problem zastarjelih podataka (concurrency)

Korisnik otvori edit formu, ode na pauzu, vrati se nakon 20 minuta i klikne Save. U međuvremenu je drugi korisnik promijenio isti zapis.

Rizik: spremanje stare forme **pregažene** tuđe promjene.

Rješenje — verzioniranje (timestamp):
1. Kod otvaranja forme poslati i informaciju o verziji zapisa
2. Kod spremanja server uspoređuje verziju iz forme s aktualnom verzijom u bazi
3. Ako se verzije razlikuju → spremanje se odbija
4. Korisniku se prikaže poruka da su podaci promijenjeni

Za vježbu nije obvezno implementirati, ali važno je razumjeti problem.

---

## `[ActionName]` atribut

**Problem:** C# ne dopušta dvije metode s istim potpisom (GET i POST Edit s parametrom `int id`).

```csharp
// Ovo se NE MOŽE prevesti:
public ActionResult Edit(int id) { ... }

[HttpPost]
public ActionResult Edit(int id) { ... }
```

**Rješenje:** Različita imena metoda u C#, isti URL kroz `[ActionName]`:

```csharp
[ActionName("Edit")]
public ActionResult EditGet(int id)
{
    // GET — prikaz forme
}

[HttpPost]
[ActionName("Edit")]
public ActionResult EditPost(int id)
{
    // POST — obrada forme
    this.TryUpdateModel(...);
}
```

Obje akcije odgovaraju na isti URL (`/Controller/Edit`), razlikuju se samo po HTTP metodi.
