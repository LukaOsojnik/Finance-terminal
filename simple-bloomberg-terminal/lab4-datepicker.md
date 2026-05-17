# Datumska kontrola (partial view)

---

## Problem s formatom datuma

Format datuma ovisi o jeziku (CultureInfo):
- US: `MM/dd/yyyy`
- Hrvatska: `dd.MM.yyyy`

Problem: korisnik u pregledniku ima `hr`, server očekuje `en` → parsiranje neuspješno.

Dodatni problem: C# format `dd.MM.yyyy` odgovara klijentskom `dd.mm.yyyy` — **razlika veliko/malo slovo za mjesece** (`MM` vs `mm`).

---

## Zahtjevi za vježbu

- Napraviti preko **partial view**
- Primijeniti na **svim mjestima** gdje se koristi datum
- Osigurati rad na **hr + en** formatu (ovisno o postavkama preglednika)
- **NE koristiti** default `input type="date"` iz browsera — mora biti JS plugin ili kompletno vlastiti kod

---

## Request Localization

Preglednik šalje informaciju o jeziku u svakom HTTP zahtjevu (`Accept-Language` header).

ASP.NET Core može automatski prepoznati jezik klijenta.

### Konfiguracija u `Program.cs`

```csharp
var supportedCultures = new[]
{
    new CultureInfo("hr"),
    new CultureInfo("en-US")
};

app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture("hr"),
    SupportedCultures = supportedCultures,
    SupportedUICultures = supportedCultures
});

app.MapControllerRoute(...)
```

Što ovo definira:
- Podržani jezici: **hrvatski** i **engleski**
- Ako nije drukčije specificirano → pretpostavlja se **hrvatski**
- Dovoljno je koristiti dvoslovne kodove (`hr`, `en`) za format datuma

> **Važno:** `UseRequestLocalization()` mora biti **prije** `UseEndpoints()` / `MapControllerRoute()`!

### Dohvat trenutne kulture

```csharp
System.Globalization.CultureInfo.CurrentCulture    // format datuma, brojeva...
System.Globalization.CultureInfo.CurrentUICulture   // prijevod UI stringova
```

`Culture` objekt sadrži format datuma, decimalnih brojeva itd. — pregledno u debuggeru.

---

## Odabir date picker kontrole

Tri pristupa:

| Pristup | Prednost | Nedostatak |
|---|---|---|
| **Native HTML** (`input type="date"`) | Jednostavan | Nije dovoljno fleksibilan |
| **JavaScript plugin** | Gotovo rješenje | Održavanje, kompatibilnost s frameworkom |
| **Custom kontrola (AI-assisted)** | Prilagođeno potrebama | Treba dobro testirati |

Danas je realna opcija napraviti vlastitu kontrolu uz pomoć AI alata.

---

## Što testirati kod date picker kontrole

- Format datuma (`dd.MM.yyyy` vs `MM/dd/yyyy`)
- Rad s **hrvatskom i engleskom** kulturom
- Validacija **praznih** i **neispravnih** vrijednosti
- Ponašanje kod **edit forme** (prikaz postojećeg datuma)
- **Spremanje** vrijednosti na server (ispravno parsiranje)

---

## Partial view pristup

Datepicker se implementira kao partial view kako bi se mogao **ponovno koristiti** na svim formama koje imaju datumsko polje.

Pattern:
1. Kreirati partial view (npr. `_DatePicker.cshtml`)
2. Definirati model koji partial prima (vrijednost datuma, format, ID polja...)
3. Uključiti partial u svaki view koji treba datum (`<partial name="_DatePicker" model="..." />`)
4. JS logiku držati u zasebnoj `.js` datoteci ili unutar partiala

---

## Preporučeni način rada s AI alatima

AI alati su najkorisniji kad trebaju **ponoviti postojeći kvalitetan obrazac**.

### Loš pristup
- Generirati 10 stranica odjednom
- Dobiti 10 približno sličnih rješenja
- Kasnije otkriti da većina ne radi ispravno

### Bolji pristup
1. Napraviti **jednu formu do kraja**
2. Provjeriti validaciju, dropdown, edit, delete, testove
3. Popraviti stil i ponašanje
4. Tek tada tražiti od AI-a da napravi ostale forme po **istom obrascu**

AI je posebno dobar u **repliciranju postojećeg rješenja** u drugi kontekst. Zato vrijedi uložiti više vremena u prvi primjer.
