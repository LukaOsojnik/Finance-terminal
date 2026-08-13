# 4. Arhitektura sustava

Poglavlje opisuje sustav izrađen kao proof of concept ekstrakcije strukturiranih podataka iz
financijskih izvješća. Sustav čita godišnja izvješća američkih javnih društava objavljena u sustavu
EDGAR i iz njih gradi zapise o izvorima prihoda, izvorima troška i objavljenim rizicima.

## 4.1. Pregled sustava i tijek obrade

Sustav radi nad jednim dokumentom i njegovim pripadajućim strukturiranim prilogom. Ne pretražuje web
kao izvor podataka za ekstrakciju.

**Ulaz.** Oznaka društva (CIK), oznaka objave (accession number) i naziv glavnog dokumenta. Iz toga
se dohvaćaju dva izvora: godišnje izvješće na obrascu 10-K u HTML obliku i označene XBRL činjenice
iste objave. XBRL je strojno čitljiv prilog objave u kojem izdavatelj sam označava iznose iz
financijskih izvještaja oznakama propisane taksonomije. Prilog je dio iste objave i dohvaća se
zajedno s dokumentom, pa sustav i dalje ne poseže za vanjskim izvorima podataka.

**Izlaz.** Zapisi u tri entiteta izvora (`RevenueSource`, `CostSource`, `CompanyRisk`), zapis objave
(`Filing`) i zapisi dokaza po polju (`SourceFieldReview`). Entitetski model opisan je u odjeljku 4.2.

Obrada ide u šest koraka:

1. dohvat dokumenta i označenog priloga (odjeljak 4.3.1),
2. priprema teksta uz očuvanje tablica (odjeljak 4.3.2),
3. pronalaženje granica Itema i podnaslova (odjeljak 4.3.3),
4. trijaža podnaslova (odjeljak 4.3.4),
5. podjela odabranih podnaslova na odsječke i njihovo paralelno čitanje (odjeljci 4.3.5 i 4.4.1),
6. objedinjavanje nalaza i priprema zapisa (odjeljak 4.4.2).

Prva tri koraka ne koriste jezični model.

```
EDGAR objava
     │
     ├──────────────────────────────┐
     ▼                              ▼
glavni dokument (HTML)        XBRL činjenice
     │                              │
     ▼                              │
priprema teksta                     │
(tablice → HTML, ostalo → tekst)    │
     │                              │
     ▼                              │
granice Itema + podnaslovi          │
     │                              │
     ▼                              │
trijaža podnaslova (1 poziv)        │
     │                              │
     ▼                              │
odsječci (do 4000 znakova)          │
     │                              │
     ▼                              │
agenti-radnici (do 6 paralelno)     │
     │                              │
     ▼                              │
objedinjavanje → sažetak nalaza     │
     └──────────────┬───────────────┘
                    ▼
             vodeći agent
                    │
                    ▼
        blok za pohranu (JSON)
                    │
                    ▼
      potvrda korisnika → zapis u bazi
```

Slika 1. Tijek obrade jednog izvješća.

Sustav gradi tri vrste zapisa. Nazivaju se čvorovima ekstrakcije: prihod, trošak i rizik. Sva tri
čvora dijele isti motor obrade. Razlikuju se u četiri točke: koje dijelove izvješća čitaju, koji im
je prompt, koji šifrarnik klasifikacije koriste i u koji se entitet zapis pohranjuje.

```csharp
public enum ExtractionNode { REVENUE, COST, RISK }

// Koje Iteme obrasca 10-K pojedini čvor čita.
public static string[] ItemsFor(ExtractionNode node) => node switch
{
    ExtractionNode.RISK => ["1A", "7A"],
    ExtractionNode.COST => ["1", "7", "8"],
    _                   => ["7", "8"],
};
```

Isječak programskog koda 1. Čvor ekstrakcije i Itemi koje čita.

Zapis se ne upisuje automatski. Vodeći agent priprema blok za pohranu, a upis se izvodi tek nakon
što korisnik potvrdi stavku. Sustav je time alat za ekstrakciju uz nadzor, a ne automatski uvoznik.

## 4.2. Entitetski model

Podaci se pohranjuju u tri entiteta izvora, po jedan za svaki čvor. Uz njih stoje entitet objave i
entitet dokaza po polju. Svi se vežu na entitet društva (`Company`).

```
                    ┌───────────┐
                    │  Company  │
                    └─────┬─────┘
        ┌─────────────────┼─────────────────┬──────────────┐
        │ 1:N             │ 1:N             │ 1:N          │ 1:N
        ▼                 ▼                 ▼              ▼
┌───────────────┐ ┌──────────────┐ ┌──────────────┐  ┌──────────┐
│ RevenueSource │ │  CostSource  │ │ CompanyRisk  │  │  Filing  │
└───────┬───────┘ └──────┬───────┘ └──────┬───────┘  └────┬─────┘
        │ 1:N            │ 1:N            │ 1:N           │
        └────────────────┴───┬────────────┘               │ 1:N
                             ▼                            │
                  ┌─────────────────────┐                 │
                  │  SourceFieldReview  │◀────────────────┘
                  └─────────────────────┘
```

Slika 2. Odnosi među entitetima.

### 4.2.1. Entiteti izvora

Prihod i trošak dijele istu strukturu. Razlikuju se samo u tipu klasifikacije.

| Polje | Tip | Opis |
|---|---|---|
| `Id` | `long` | primarni ključ |
| `CompanyId` | `long` | strani ključ na `Company`, obvezan |
| `SourceType` / `CostBase` | nabrojeni tip | klasifikacija zapisa |
| `Name` | `string` | naziv stavke, segmenta, proizvoda ili protustranke |
| `Value` | `double?` | iznos u apsolutnim dolarima |
| `Percentage` | `double?` | udio u ukupnom prihodu ili trošku, 0–100 |
| `RelatedCompanyId` | `long?` | strani ključ na `Company` — protustranka |
| `Reference` | `string?` | doslovan odlomak iz kojeg je cijeli zapis izveden |
| `DataSource` | `DataSource?` | podrijetlo podatka (`EDGAR`, `MANUAL`, …) |
| `Status` | `ContributionStatus` | `Approved`, `Pending` ili `Rejected` |
| `ContributedByUserId` | `string?` | korisnik koji je zapis predložio |
| `SupersedesId` | `long?` | zapis koji ovaj prijedlog zamjenjuje |
| `DeletedAt` | `DateTime?` | meko brisanje |

Tablica 1. Polja entiteta `RevenueSource` i `CostSource`.

`CompanyRisk` nema iznos, postotak ni protustranku. Umjesto njih ima `Scope` tipa `RiskScope` i
`Note` tipa `string?` s kratkim opisom rizika. Ostala polja su ista.

Dopuštenost prazne vrijednosti nosi značenje. Podatak koji u izvješću nije naveden ostaje `null`, a
ne nula. Nula bi bila tvrdnja da iznos postoji i da je nula.

### 4.2.2. Šifrarnici klasifikacije

Klasifikacija je nabrojeni tip, ne slobodan tekst. Postoji po jedan šifrarnik za svaki čvor. Prihod
ima vrijednosti `CUSTOMER`, `SEGMENT`, `REGION` i `PRODUCT`. Trošak ima `COGS`, `OPEX` i
`TOTAL_COSTS`. Rizik ima `MACROECONOMIC`, `INDUSTRY`, `BUSINESS`, `LEGAL_REGULATORY`, `FINANCIAL` i
`GENERAL`.

Isti nizovi znakova pojavljuju se u promptu agenta. Model vraća naziv vrijednosti, a kod ga
pretvara u nabrojeni tip prije pohrane (odjeljak 4.5.2).

### 4.2.3. Entitet objave

| Polje | Tip | Opis |
|---|---|---|
| `Id` | `long` | primarni ključ |
| `CompanyId` | `long` | strani ključ na `Company` |
| `AccessionNumber` | `string` | oznaka objave u sustavu EDGAR, jedinstvena |
| `Form` | `string?` | obrazac, primjerice `10-K` |
| `FilingDate` | `DateTime?` | datum objave |
| `PrimaryDocUrl` | `string?` | poveznica na glavni dokument |

Tablica 2. Polja entiteta `Filing`.

Identitet objave je njezina oznaka u sustavu EDGAR, koja je globalno jedinstvena. Nad tim poljem
stoji jedinstveni indeks, pa se ista objava pohranjuje jednom i dijeli među svim zapisima koji je
navode kao izvor.

### 4.2.4. Entitet dokaza

`SourceFieldReview` je zapis dokaza za **jedno polje jednog zapisa**. Zbog toga jedan izvor prihoda
može imati više dokaza, po jedan za naziv, iznos, postotak i tako dalje.

| Polje | Tip | Opis |
|---|---|---|
| `Id` | `long` | primarni ključ |
| `CompanyId` | `long` | strani ključ na `Company` |
| `Relation` | `RelationKind` | kojem čvoru dokaz pripada (`REVENUE`, `COST`, `RISK`) |
| `RevenueSourceId` | `long?` | strani ključ na zapis prihoda |
| `CostSourceId` | `long?` | strani ključ na zapis troška |
| `CompanyRiskId` | `long?` | strani ključ na zapis rizika |
| `Field` | `ReviewableField` | koje polje zapisa ovaj dokaz potkrepljuje |
| `ReferenceSnapshot` | `string` | doslovan isječak izvornog teksta |
| `ReferencedValue` | `string?` | vrijednost pročitana iz tog isječka |
| `FilingId` | `long?` | objava iz koje isječak potječe |

Tablica 3. Polja entiteta `SourceFieldReview`.

Točno jedan od tri strana ključa prema zapisima izvora je popunjen, a preostala dva su prazna.
Pravilo je nametnuto u bazi (odjeljak 4.2.5).

```csharp
public enum ReviewableField
{ VALUE, PERCENTAGE, NAME, RELATED_COMPANY, CLASSIFICATION, NOTE }
```

Isječak programskog koda 2. Polja zapisa koja se mogu potkrijepiti dokazom.

Veza na objavu stoji na dokazu, a ne na zapisu izvora. Razlog je granularnost. Jedan izvor prihoda
može imati iznos potkrijepljen jednom objavom, a udio drugom. Da veza stoji na zapisu, taj se slučaj
ne bi mogao prikazati.

### 4.2.5. Ograničenja u bazi

Pravila entitetskog modela nametnuta su na razini baze, a ne u aplikacijskom kodu.

```csharp
// Točno jedan od tri strana ključa smije biti popunjen.
e.ToTable(t => t.HasCheckConstraint(
    "CK_SourceFieldReview_OneSource",
    "((RevenueSourceId IS NOT NULL) + (CostSourceId IS NOT NULL) + (CompanyRiskId IS NOT NULL)) = 1"));

// Najviše jedan važeći dokaz po zapisu i polju.
e.HasIndex(r => new { r.RevenueSourceId, r.Field }).IsUnique();
```

Isječak programskog koda 3. Ograničenja nad entitetom dokaza.

Provjera sprječava dokaz koji istovremeno pripada prihodu i trošku. Jedinstveni indeks znači da novi
dokaz zamjenjuje prethodni, umjesto da se dokazi za isto polje gomilaju; jednaka dva indeksa
postavljena su nad zapisima troška i rizika. Jedinstveni indeks nad oznakom objave sprječava
dvostruki upis iste objave.

Brisanje zapisa izvora je meko: postavlja se `DeletedAt`, a redak ostaje u bazi. Strani ključevi
prema objavi i prema zapisima izvora postavljeni su na `Restrict`, pa se referencirani redak ne može
tvrdo obrisati ispod dokaza koji ga navodi.

## 4.3. Priprema dokumenta

### 4.3.1. Učitavanje

Dokument se dohvaća s EDGAR poslužitelja prema oznaci društva i oznaci objave. Izvorni oblik je HTML
namijenjen prikazu u pregledniku. Dohvaćeni dokument i pripremljeni odsječci drže se u međuspremniku
trideset minuta, jer jedan prolaz ekstrakcije više puta poseže za istim izvješćem.

### 4.3.2. Dobivanje teksta

Ravnanje HTML-a u čisti tekst uništava tablice. `InnerText` nad retkom tablice ne stavlja ništa
između ćelija, pa redak bilance izlazi kao `Cash and cash equivalents$ 29,965$ 23,646`. Stupci
nestaju.

Zato se tablice prije ravnanja izdvajaju i zasebno oblikuju. Zadržavaju se redak, stupac i oznake
`colspan` i `rowspan`, a uklanja se sve što je samo prikaz: stilovi, razredi i poveznice. Tablica
tako ostaje tablica, u obliku koji može izraziti spojenu ćeliju. To je nužno jer zaglavlje
financijske tablice redovito natkriljuje više stupaca. Ostatak dokumenta ravna se u tekst, a tablice
se vraćaju na svoja mjesta.

```html
<table><tr><td colspan="3"></td><td colspan="15">Dec 27, 2025</td></tr>
<tr><td colspan="3">Year Ended ($ In Millions)</td>…<td colspan="3">CCG</td>…<td colspan="3">Total</td></tr>
<tr><td colspan="3">Revenue</td>…<td>$32,228</td>…<td>$49,147</td></tr>
<tr><td colspan="3">Operating income</td>…<td>$9,317</td>…<td>$12,739</td></tr></table>
```

Isječak programskog koda 4. Tablica poslovnih segmenata onako kako je prima agent-radnik.

Nisu sve tablice podatkovne. EDGAR objave često omataju obični tekst u `<table>` radi poravnanja.
Tablica s manje od četiri ćelije ili bez ijedne brojčane ćelije sravnjuje se u tekst kao i ostatak
dokumenta, pa njezin sadržaj i dalje dolazi do modela.

Financijski izvještaji dohvaćaju se iz drugog izvora. EDGAR uz svaku objavu iz predanih XBRL podataka
generira i skup izvještaja, po jedan u zasebnoj datoteci, a njihov popis stoji u indeksu
`FilingSummary.xml`. To su financijski izvještaji već rekonstruirani u ispravne tablice, s mjernom
jedinicom u naslovu i s imenom pojma taksonomije uz naziv svakog retka. Item 8 čita te izvještaje,
ostali Itemi glavni dokument. Odabir se provodi nad indeksom, prije ijednog preuzimanja, jer jedna
objava indeksira između sedamdeset i sto izvještaja.

### 4.3.3. Granice Itema

Obrazac 10-K podijeljen je na numerirane stavke označene riječju *Item* i rednim brojem. Granice se
traže regexom, na dva načina: po oznaci Itema na početku retka i po propisanom naslovu stavke. Drugi
je način nužan jer dio izdavatelja u tijelu dokumenta ne ispisuje oznaku, nego samo naslov. Naslovi
su propisani regulativom, pa su jednako pouzdana oznaka granice kao i redni broj.

```csharp
private static readonly (string Num, string Title)[] ItemTitles =
[
    ("1A", @"Risk\s+Factors"),
    ("7A", @"Quantitative\s+and\s+Qualitative\s+Disclosures?\s+About\s+Market\s+Risk"),
    ("7",  @"Management'?.?s\s+Discussion\s+and\s+Analysis(\s+of\s+Financial\s+Condition.*)?"),
    ("8",  @"Financial\s+Statements\s+and\s+Supplementary\s+Data"),
];
```

Isječak programskog koda 5. Propisani naslovi stavki po kojima se traže granice.

Dva pravila razrješavaju višestruka pojavljivanja. Jedna oznaka pojavljuje se i u sadržaju na
početku i na mjestu same stavke, pa se uzima ono pojavljivanje iza kojeg slijedi najviše teksta.
Stavka završava na sljedećoj *različitoj* oznaci, jer izdavatelji naslov stavke ponavljaju kao
zaglavlje svake stranice.

Ovdje je vidljiva uloga koja je rule-based metodama preostala. Regex u sustavu ne izvlači nijednu
vrijednost. Traži samo strukturu dokumenta. Oblik oznake Itema propisan je i stabilan, pa pravilo na
njemu ne otkazuje. Vrijednosti unutar Itema nisu propisane i njih preuzima model.

### 4.3.4. Trijaža po naslovima

Unutar Itema podnaslovi se prepoznaju po podebljanom retku. Godišnje izvješće daje reda veličine
stotinu podnaslova, a većina ne sadrži podatak koji aktivni čvor traži. Slanje svih odsječaka modelu
bilo bi izvedivo, ali skupo.

Zato se uvodi korak trijaže. Modelu se šalju samo naslovi, numerirani, bez teksta ispod njih. Model
vraća redne brojeve podnaslova vrijednih čitanja.

```csharp
// Ulaz u model: "0: [Item 7] Segment Operating Results" …
// Izlaz modela: {"ids":[0,3,7,12]}
var answer = await _llm.CompleteAsync(
    TriageSystemFor(node), $"Headings:\n{list}",
    maxTokens: 800, jsonObject: true, fast: true, ct: ct);
var ids = ParseIds(answer, headings.Count);
if (ids.Count > 0) return ids;
...
return Enumerable.Range(0, headings.Count).ToList();   // trijaža pala → čitaj sve
```

Isječak programskog koda 6. Korak trijaže i njegov postupak povratka.

Trijaža je jedan poziv i troši malo tokena, jer naslovi su kratki. Ne smije biti jedina brana: ako
poziv ne uspije ili model vrati prazan popis, sustav čita sve podnaslove. Otkaz trijaže poskupljuje
obradu, ali je ne prekida.

Prepoznavanje podnaslova ovisi o načinu na koji izdavatelj oblikuje dokument. Kod izdavatelja koji
podnaslove ističe veličinom i bojom gotovo nijedan ne bude prepoznat. Zato Item koji daje manje od
pet podnaslova ne ide kroz trijažu, nego se čita sekvencijalno.

### 4.3.5. Podjela na odsječke

Godišnje izvješće prelazi sto stranica. Cijeli tekst ne stane u jedan poziv modela, a i kad bi
stao, točnost bi pala jer modeli slabije koriste podatke smještene u sredini dugog konteksta [12].
Zato se dokument dijeli.

Odabrani podnaslovi pakiraju se u odsječke do 4000 znakova, što je otprilike tisuću tokena po pozivu.
Granica odsječka je prazan redak, a odlomak se nikada ne prekida na pola.

```csharp
// Tablica ide cijela; samo obični odlomak se reže.
if (para.Length > MaxChunkChars)
    yield return isTable ? para : para[..MaxChunkChars];

// Tablica koja pada u novi odsječak povlači uvodnu rečenicu sa sobom.
if (current.Length + para.Length > MaxChunkChars)
{
    yield return current.ToString();
    current.Clear();
    if (isTable && prev is { Length: <= CaptionMaxChars }) current.Append(prev);
}
```

Isječak programskog koda 7. Dva pravila kojima se tablice štite pri podjeli na odsječke.

Prvo pravilo sprječava rezanje tablice, jer bi rezanje izgubilo retke s iznosima. Drugo pravilo veže
tablicu uz uvodnu rečenicu. Rečenica koja tablicu najavljuje zaseban je odlomak („The following
table shows net sales by reportable segment … (dollars in millions):"), a u njoj stoje predmet i
mjerna skala. Tablica koja bi bez nje pala na granicu odsječka bila bi mreža golih brojeva.

Item 8 ne dijeli se iz dokumenta. Njegovi odsječci nastaju iz izvještaja opisanih u 4.3.2, gdje je
svaka datoteka već jedna cjelovita financijska tablica.

## 4.4. Ekstrakcija u dvije razine

Jezgra sustava je podjela posla na dvije razine. Donja razina čita dokument u dijelovima. Gornja
razina ne čita dokument, nego objedinjuje nalaze donje razine.

### 4.4.1. Donja razina — paralelni agenti-radnici

Svaki odsječak čita jedan poziv modela. Pozivi su međusobno neovisni, pa se izvode paralelno.
Istovremeno se izvodi najviše šest poziva. Ograničenje postoji zbog ograničenja učestalosti zahtjeva
prema pružatelju usluge.

```csharp
private const int MaxParallel = 6;

private async Task<List<ExtractionSuggestion>> ScanChunksAsync(
    IReadOnlyList<FilingChunk> chunks, ExtractionNode node, ...)
{
    using var gate = new SemaphoreSlim(MaxParallel);
    var perChunk = await Task.WhenAll(
        chunks.Select((c, i) => ScanChunkAsync(c, i, node, gate, onProgress, ct)));
    ...
}
```

Isječak programskog koda 8. Paralelno izvođenje uz semafor kao ograničenje istovremenosti.

Semafor je propusnica: zadatak čeka slobodno mjesto prije poziva modela i oslobađa ga nakon
odgovora. Broj zadataka jednak je broju odsječaka, ali broj istovremenih poziva nikad ne prelazi
šest.

Otkaz jednog poziva ne ruši prolaz. Odsječak čiji je poziv pao vraća prazan popis, a ostali se
nastavljaju.

### 4.4.2. Gornja razina — vodeći agent

Nalazi svih radnika oblikuju se u sažetak. Sažetak sadrži naziv, klasifikaciju, iznos, postotak i
protustranku svakog kandidata, oznaku Itema iz kojeg potječe te doslovne isječke po poljima.

```
PARALLEL-SCAN FINDINGS (revenue candidates the worker agents pulled from the filing):
- Client Computing Group [SEGMENT] | value=32228000000 | from Item 8
    proof.name: "Client Computing Group"
    proof.value: "Revenue … $32,228"
```

Isječak programskog koda 9. Oblik sažetka nalaza koji prima vodeći agent.

Sažetak se zajedno s označenim XBRL činjenicama predaje vodećem agentu. Njegov zadatak nije ponovno
čitanje izvješća, nego spajanje dvaju izvora i priprema zapisa.

Sustav ne ovisi o jednom modelu. Pružatelja usluge bira korisnik, a svaki pružatelj nudi više modela
različite cijene i sposobnosti. Sustav koristi dvije razine: bržu i jeftiniju za trijažu i agente-
radnike te snažniju za vodećeg agenta.

Podjela slijedi iz raspodjele poziva. Jedna obrada pokreće jedan poziv trijaže, više desetaka poziva
radnika i jedan poziv vodećeg agenta. Prve dvije skupine čine gotovo sve pozive, a zadatak im je
uzak. Vodeći agent je jedan poziv i traži prosudbu.

## 4.5. Prompt i nametanje izlazne sheme

### 4.5.1. Traženje strukturiranog izlaza

Strukturirani izlaz nameće se na tri mjesta istovremeno, jer nijedno samo za sebe nije dovoljno.

**Prvo, na razini pružatelja usluge.** Zastavica `jsonObject` postavlja parametar `response_format`,
čime pružatelj usluge jamči sintaktički ispravan JSON. Jamstvo ne obuhvaća shemu, a kod nekih
pružatelja ne postoji uopće.

**Drugo, u promptu.** Prompt doslovno ispisuje traženu shemu s praznim vrijednostima.

```csharp
"Reply with JSON only, no prose, no code fences: " +
"{\"sources\":[{\"name\":\"\",\"classification\":\"\",\"value\":null,\"percentage\":null," +
"\"related_company\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
"\"classification\":null,\"related_company\":null}}]}. If the excerpt names no revenue " +
"source, reply {\"sources\":[]}."
```

Isječak programskog koda 10. Ispis izlazne sheme u promptu agenta-radnika.

Sva tri čvora vraćaju istu vanjsku strukturu `{"sources":[…]}`. Razlikuju se u poljima: prihod i
trošak imaju iznos, postotak i protustranku, a rizik umjesto njih ima bilješku i opseg. Zbog
zajedničke vanjske strukture parsiranje odgovora dijeli se među čvorovima.

**Treće, pri čitanju odgovora.** Kod odgovor čita tolerantno. Reže od prve otvorene do posljednje
zatvorene vitičaste zagrade, prihvaća iznos i kao broj i kao niz znakova te spašava odgovor prekinut
zbog ograničenja duljine izlaza — zadržava zatvorene zapise i odbacuje samo posljednji nepotpuni.

Prompt sadrži i zahtjev da model za svako popunjeno polje vrati doslovan isječak izvornog teksta iz
kojeg je vrijednost preuzeta. Prazno polje nema isječak. Time vrijednost više nije samo tvrdnja
modela, nego tvrdnja uz mjesto u dokumentu koje se može provjeriti. Ti isječci završavaju u polju
`ReferenceSnapshot` entiteta dokaza iz odjeljka 4.2.4.

Svi promptovi u sustavu su zero-shot. Prazna shema nije riješen primjer, jer nije uparena s ulazom.
Ona propisuje oblik, a ne rješenje.

### 4.5.2. Normalizacija vrijednosti

Normalizacija je podijeljena između prompta i koda. Podjela slijedi iz toga ovisi li pravilo o
kontekstu u dokumentu.

**U promptu** se traži ono što ovisi o dokumentu:

| Traženo u promptu | Formulacija |
|---|---|
| mjerna skala i valuta | `value (revenue in absolute US dollars — scale any 'in thousands/millions' to the full number; null if not stated)` |
| raspon postotka | `percentage (share of total revenue 0-100, null if not stated)` |
| šifrarnik | `classification (exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT)` |
| bez izmišljanja | `Return ONLY the sources clearly evidenced in THIS excerpt — do not guess or carry over outside knowledge` |

Tablica 4. Zahtjevi normalizacije u promptu agenta-radnika.

Mjerna skala mora ići u prompt. Financijske tablice iznose prikazuju u tisućama ili milijunima, uz
napomenu iznad tablice. Tu napomenu vidi samo model, zajedno s brojem.

**U kodu** ostaje ono što ne ovisi o dokumentu. Pripadnost šifrarniku mora vrijediti bezuvjetno, pa
se ne prepušta modelu. Prompt traži vrijednost iz šifrarnika, ali se ta tvrdnja ne uzima na riječ.
Prije pohrane kod pokušava naziv koji je model vratio pretvoriti u pripadajući nabrojeni tip. Ako
naziv ne odgovara nijednoj vrijednosti šifrarnika, zapis se ne pohranjuje.

To je jedina točka u sustavu na kojoj otkaz znači gubitak podatka umjesto smanjene količine
podataka, jer bi neispravna klasifikacija ušla u bazu.

## 4.6. Spajanje rezultata i razrješavanje sukoba

Sukob se javlja na dvije razine i rješava se na dva različita načina.

### 4.6.1. Sukob između odsječaka

Kandidati iz svih odsječaka spajaju se u jedan popis, a duplikati se uklanjaju po nazivu.

```csharp
var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
foreach (var list in perChunk)
    foreach (var s in list)
        if (!string.IsNullOrWhiteSpace(s.Name) && !byName.ContainsKey(s.Name))
            byName[s.Name] = s;
```

Isječak programskog koda 11. Objedinjavanje kandidata iz odsječaka.

Pravilo prioriteta je jednostavno: ako dva odsječka daju zapis istog naziva, zadržava se prvi.
Odsječci su poredani po pripadnosti Itemima, a redoslijed Itema određen je u `ItemsFor` (isječak 1) i
poredan po prioritetu za taj čvor. Prvi zapis stoga dolazi iz Itema koji je za aktivni čvor
prioritetan.

Pravilo je ponovljivo, ali ne ocjenjuje koja je vrijednost točnija. Stvarna provjera vrijednosti ne
događa se ovdje.

### 4.6.2. Determinizam pri paralelnom izvođenju

Paralelno izvođenje ne narušava ovo pravilo. `Task.WhenAll` iz isječka 8 vraća polje rezultata u
redoslijedu **ulaznih zadataka**, a ne u redoslijedu njihova dovršetka. Petlja nad `perChunk` zato
uvijek ide redom odsječaka, bez obzira na to koji je radnik prvi odgovorio.

Time je korak objedinjavanja determinističan. Uz isti skup odsječaka i iste odgovore modela, rezultat
je uvijek isti popis u istom redoslijedu. Preostali izvor nedeterminizma nije paralelizam, nego sam
jezični model: dva poziva nad istim odsječkom mogu dati različit odgovor.

### 4.6.3. Sukob između pročitane i označene vrijednosti

Druga razina sukoba je između vrijednosti koju je model pročitao iz teksta i vrijednosti koju je
izdavatelj označio u XBRL prilogu. Načelo je podjela odgovornosti po polju.

| Polje | Izvor koji ima prednost |
|---|---|
| `Value` | označeni XBRL podatak |
| `Name`, `RelatedCompany`, klasifikacija | tekst koji su pročitali agenti-radnici |

Tablica 5. Redoslijed prednosti pri spajanju dvaju izvora.

Pravilo je zapisano u promptu vodećeg agenta.

```csharp
"The tagged XBRL figures are the audited numbers — PREFER them for `value`; use the workers' " +
"prose for the name, segment and customer. When a prose figure disagrees with the tagged " +
"figure for the same line, flag it rather than silently choosing."
```

Isječak programskog koda 12. Pravilo prednosti u promptu vodećeg agenta.

Ovaj se sukob ne rješava automatski. Ako se iznos iz teksta razlikuje od označenog iznosa za istu
stavku, agent to mora navesti, a ne tiho odabrati jedan od njih. Tiho odbacivanje krilo bi činjenicu
da izvori ne daju isti odgovor, a odluku o tome koji je izvor točan sustav prepušta korisniku.

Sažeto: sukob unutar jednog izvora rješava se determinističkim pravilom u kodu, a sukob između dvaju
različitih izvora se označava i prepušta korisniku.

## 4.7. Provjerljivost izdvojene vrijednosti

Svaka pohranjena vrijednost ima uz sebe doslovan isječak izvornog teksta iz kojeg je izvedena.
Mehanizam je opisan u prethodnim odjeljcima: prompt od modela traži isječak za svako popunjeno polje
(odjeljak 4.5.1), a isječak se pohranjuje u zaseban zapis dokaza vezan uz to jedno polje
(odjeljak 4.2.4). Ovdje se ta dva dijela zatvaraju u cjelinu.

Vodeći agent ne upisuje u bazu. On priprema blok za pohranu, a upis se izvodi tek nakon što korisnik
potvrdi pojedinu stavku. Sustav time nema put kojim bi vrijednost ušla u bazu bez ljudske odluke.

Uz predloženu vrijednost korisnik vidi i isječak iz kojeg je ona izvedena. Potvrda zato nije slijepa:
korisnik uspoređuje predloženu vrijednost s tekstom izvješća prije nego što je prihvati. Isječak
ostaje pohranjen i nakon upisa, pa je zapis provjerljiv i kasnije, neovisno o sjednici u kojoj je
nastao.

Time je ispunjen drugi uvjet postavljen u odjeljku 3.5 — mogućnost povezivanja izdvojene vrijednosti
s mjestom u dokumentu. Prvi uvjet, izmjerena točnost, obrađuje se u 5. poglavlju.

## 4.8. Funkcije izvan opsega vrednovanja

Aplikacija ima i funkcije koje nisu ekstrakcija iz dokumenta. Prva je pretraga weba: njome se
popunjavaju profili društava bez burzovne oznake, otkrivaju protustranke po nazivu i dohvaćaju
sastavi burzovnih indeksa. Druga je klasifikacija djelatnosti društva, koja društvu pridružuje
granu i podgranu prema standardnoj klasifikaciji.

Te funkcije ne sudjeluju u ekstrakciji podataka iz izvješća. Ne čitaju dokument objave, ne koriste
opisani motor obrade i ne pišu u entitete izvora opisane u odjeljku 4.2. Zato ne ulaze u vrednovanje
u 5. poglavlju.

Razlog razgraničenja je metodološki. Rad ocjenjuje ekstrakciju iz zadanog dokumenta, gdje se svaka
izdvojena vrijednost može usporediti s mjestom u tom dokumentu. Podatak dobiven pretragom weba nema
dokument nad kojim bi se provjerio, pa nije usporediv s ostatkom rezultata.
