# 4. Arhitektura sustava

Ovo poglavlje opisuje sustav izrađen kao proof of concept ekstrakcije strukturiranih podataka iz
financijskih izvješća. Sustav čita godišnja izvješća američkih javnih društava objavljena u sustavu
EDGAR i iz njih gradi zapise o izvorima prihoda, izvorima troška i objavljenim rizicima. Opisuju se
tijek obrade, entitetski model podataka i algoritam ekstrakcije korak po korak.

Sustav radi nad jednim dokumentom i njegovim pripadajućim strukturiranim prilogom. Ne pretražuje web
kao izvor podataka za ekstrakciju.

## 4.1. Pregled i tijek obrade

Ekstrakcija se odvija u šest koraka. Prva tri ne koriste jezični model. Dokument se dohvaća i
priprema, pri čemu se tablice izdvajaju i zadržavaju vlastitu strukturu, a ostatak se ravna u čisti
tekst; regexom se pronalaze granice propisanih stavki, a tekst se dijeli na odsječke. Tek zatim
nastupa model: jedan jeftin poziv bira odsječke vrijedne čitanja, paralelni agenti-radnici čitaju
odabrane odsječke, a jedan vodeći agent objedinjuje njihove nalaze s tagiranim XBRL podacima i
priprema zapise za pohranu.

```
EDGAR objava
     │
     ├────────────────────────────┐
     ▼                            ▼
glavni dokument (HTML)      indeks izvještaja
     │                            │
     ▼                            ▼
tablice → strukturni HTML,  odabrane R-datoteke
ostatak → čisti tekst             │
     │                            ▼
     ▼                      odsječci Itema 8
granice Itema + podnaslovi        │
     │                            │
     ├──▶ trijaža podnaslova      │
     │    (jedan poziv,           │
     ▼     jeftin model)          │
odabrani odsječci                 │
     │                            │
     └────────────┬───────────────┘
                  ▼
        agenti-radnici (do 6 paralelno) ──▶ JSON s dokazima
                  │
                  ▼
        objedinjavanje → sažetak nalaza
                  │
     ┌────────────┴────────────┐
     ▼                         ▼
tagirane XBRL činjenice   sažetak nalaza
     └────────────┬────────────┘
                  ▼
           vodeći agent (jak model)
                  │
                  ▼
        blok za pohranu → entiteti + dokazi po polju
```

Slika 1. Tijek obrade jednog izvješća.

Sustav gradi tri vrste zapisa. Nazivaju se čvorovima ekstrakcije: prihod, trošak i rizik. Sva tri
čvora dijele isti motor obrade. Razlikuju se u četiri točke: koje dijelove izvješća čitaju, koji im
je prompt, koji šifrarnik klasifikacije koriste i u koji se entitet zapis pohranjuje. Nijedan drugi
dio sustava ne zna koji čvor je aktivan.

Godišnje izvješće predaje se na propisanom obrascu 10-K. Obrazac je podijeljen na numerirane stavke,
označene riječju *Item* i rednim brojem. Stavke naknadno umetnute u obrazac nose i slovo, pa iza
oznake `Item 1` slijedi `Item 1A`, a ne preimenovana `Item 2`. Oznaka je propisana i u radu se
zadržava u izvornom obliku, jer je istovremeno i oznaka u tekstu dokumenta koju sustav traži.

| Oznaka | Naziv u obrascu | Sadržaj |
|---|---|---|
| Item 1 | Business | opis poslovanja, izvori i dostupnost sirovina, imenovani dobavljači |
| Item 1A | Risk Factors | objavljeni faktori rizika |
| Item 7 | Management's Discussion and Analysis | komentar uprave na poslovanje i rezultate po segmentima |
| Item 7A | Market Risk | izloženost tržišnom riziku |
| Item 8 | Financial Statements and Supplementary Data | financijski izvještaji, bilješke i revizorsko izvješće |

Tablica 1. Itemi obrasca 10-K koje sustav čita.

Sustav svakom čvoru pridružuje Iteme u kojima se njegovi podaci objavljuju.

```csharp
public static string[] ItemsFor(ExtractionNode node) => node switch
{
    ExtractionNode.RISK => ["1A", "7A"],
    ExtractionNode.COST => ["1", "7", "8"],
    _                   => ["7", "8"],
};
```

Isječak programskog koda 1. Pridruživanje Itema pojedinom čvoru ekstrakcije.

Prihod i trošak čitaju se iz Itema 7 i Itema 8. Item 8 sadrži bilješku o poslovnim segmentima, koja
je jedino mjesto s revidiranim prihodom po pojedinom segmentu poslovanja. Trošak dodatno čita
Item 1, jer se ovisnost o imenovanom dobavljaču objavljuje ondje, a ne u financijskim Itemima.
Rizici se objavljuju u Itemima 1A i 7A, pa čvor rizika ne čita financijske Iteme.

## 4.2. Sloj jezičnog modela

Sustav ne ovisi o jednom modelu ni o jednom pružatelju usluge. Svaki poziv modela ide kroz omotač u
dvije razine. Donja razina je prijenos: po jedna implementacija za svakog pružatelja usluge, koja
obavija njegovo HTTP sučelje. Gornja razina je usmjerivač: on pri svakom pozivu razrješava koji se
pružatelj usluge i koji model koriste, pa poziv prosljeđuje pripadajućem prijenosu.

```csharp
// donja razina: zna pružatelja usluge i naziv modela
public interface IChatProvider
{
    Task<string> CompleteAsync(
        string model, string system, string userPrompt,
        int maxTokens, bool jsonObject, CancellationToken ct);
}

// gornja razina: isti poziv, ali bez naziva modela
public interface IChatLlm
{
    Task<string> CompleteAsync(
        string system, string userPrompt,
        int maxTokens = 4096, bool jsonObject = false, bool fast = false,
        CancellationToken ct = default);
}

// usmjerivač: gornje sučelje, u kojem opći poziv postaje poziv konkretnog modela
public async Task<string> CompleteAsync(
    string system, string userPrompt,
    int maxTokens = 4096, bool jsonObject = false, bool fast = false,
    CancellationToken ct = default)
{
    var keys = await _keys.GetAsync(ct);
    var model = fast
        ? ChatProviders.FastModel(keys.ParsingProvider)
        : ChatProviders.ResolveModel(keys.ParsingProvider, keys.ParsingModel);
    return await Provider(keys.ParsingProvider)
        .CompleteAsync(model, system, userPrompt, maxTokens, jsonObject, ct);
}
```

Isječak programskog koda 2. Dva sučelja omotača i njihovo povezivanje u usmjerivaču.

Razlika između dvaju sučelja je parametar `model`. Servisi ekstrakcije ovise samo o gornjem, pa ne
poznaju ni pružatelja usluge ni naziv modela. Predaju sistemski prompt, korisnički prompt i dvije
zastavice: `fast` bira razinu modela, a `jsonObject` traži JSON izlaz. Oba sučelja imaju i istovrsnu
metodu s prijenosom odgovora u tijeku.

Usmjerivač obavlja dvije pretvorbe. Zastavicu `fast` pretvara u naziv modela, uzimajući iz kataloga
brzi ili podrazumijevani model odabranog pružatelja usluge. Oznaku pružatelja usluge pretvara u
objekt prijenosa. Treću pretvorbu obavlja sam prijenos: opće parametre preslikava u polja koja
njegovo sučelje očekuje. Razlike koje pritom premošćuje nisu kozmetičke.

| Svojstvo poziva | DeepSeek, Kimi, OpenAI | Anthropic |
|---|---|---|
| zaglavlje autorizacije | `Authorization: Bearer` | `x-api-key` uz oznaku verzije |
| sistemski prompt | poruka s ulogom `system` | zasebno polje najviše razine |
| ograničenje broja tokena | `max_tokens`, kod OpenAI-a `max_completion_tokens` | `max_tokens`, obvezan |
| oblik odgovora | `choices[0].message.content` | niz `content` s tipiziranim blokovima |
| nametanje JSON izlaza | parametar `response_format` | ne postoji |

Tablica 2. Razlike među sučeljima pružatelja usluge koje omotač premošćuje.

Odgovor se vraća istim putem. Svaki prijenos sam vadi tekst iz vlastite omotnice i vraća običan niz
znakova, pa servis koji je poziv pokrenuo ne vidi ni jednu od navedenih razlika.

Podržana su četiri pružatelja usluge, a DeepSeek je podrazumijevani. Svaki u katalogu deklarira popis
modela koje nudi, podrazumijevani model i brzi model.

```csharp
public static readonly IReadOnlyList<ProviderInfo> Parsing =
[
    new(ChatProviderId.DeepSeek, "DeepSeek", ...,
        ["deepseek-v4-pro", "deepseek-v4-flash"], "deepseek-v4-pro", "deepseek-v4-flash"),
    new(ChatProviderId.Kimi, "Kimi (Moonshot)", ...,
        ["kimi-k2.6", "kimi-k2.5"], "kimi-k2.6", "kimi-k2.5"),
    new(ChatProviderId.OpenAi, "OpenAI", ...,
        ["gpt-5.5", "gpt-5", "gpt-5-mini"], "gpt-5", "gpt-5-mini"),
    new(ChatProviderId.Anthropic, "Anthropic", ...,
        ["claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5"],
        "claude-sonnet-4-6", "claude-haiku-4-5"),
];
```

Isječak programskog koda 3. Katalog pružatelja usluge i njihovih modela.

Podjela na dvije razine modela slijedi iz raspodjele poziva. Jedna obrada izvješća pokreće jedan
poziv trijaže, više desetaka poziva agenata-radnika i jedan poziv vodećeg agenta. Prve dvije skupine
čine gotovo sve pozive, a zadatak im je uzak, pa im se dodjeljuje brzi model. Vodeći agent je jedan
poziv i traži prosudbu, pa dobiva model koji je korisnik odabrao kao podrazumijevani. Kad bi svi
pozivi išli na snažniji model, trošak obrade jednog izvješća bi porastao, a zadatak agenta-radnika
time ne bi postao točniji.

Ključ za pristup pružatelju usluge pripada prijavljenom korisniku i razrješava se pri svakom
zahtjevu. Sustav nema zajednički ključ, a korisnik bez ključa dobiva poziv da ga unese.

Nametanje JSON izlaza provodi se izvan prompta: zastavica `jsonObject` postavlja parametar
`response_format`, čime pružatelj usluge jamči sintaktički ispravan JSON. Jamstvo ne obuhvaća shemu,
a kod Anthropica ne postoji. Zato prompt i dalje ispisuje traženu shemu, a kod odgovor čita
tolerantno: reže od prve otvorene do posljednje zatvorene vitičaste zagrade te uklanja oznaku valute,
zareze i znak postotka prije pretvorbe broja.

Vodeći agent koristi drukčiji način rada. Njegov je izlaz namijenjen za komunikaciju s korisnikom, s umetnutim
blokom za pohranu, pa se prenosi token po token radi "live" odgovora.

## 4.3. Dohvat i priprema dokumenta

Dokument se dohvaća s EDGAR poslužitelja prema oznaci društva i oznaci objave. Izvorni oblik je HTML
namijenjen prikazu u pregledniku i sustav ga obrađuje u tom obliku.

Tablice se prije obrade izdvajaju iz dokumenta i zasebno oblikuju. Zadržavaju se redak, stupac i oznake
`colspan` i `rowspan`, a uklanja se sve što je samo prikaz: stilovi, razredi, poveznice i skriveni
blokovi. Tablica tako ostaje tablica, u obliku koji može izraziti spojenu ćeliju, a to je nužno jer
zaglavlje financijske tablice redovito natkriljuje više stupaca. Ostatak dokumenta ravna se u čisti
tekst, a tablice se vraćaju na svoja mjesta.

Nisu sve tablice podatkovne. EDGAR objave često omataju obični tekst u `<table>` radi poravnanja. Takva
tablica ima manje od četiri ćelije ili nijednu brojčanu ćeliju, pa se sravnjuje u tekst kao i ostatak
dokumenta i njezin sadržaj i dalje dolazi do modela.

Unutar podatkovne tablice sanira se raspored ćelija. Izdavatelji oznaku valute i zagrade negativnog
iznosa smještaju u zasebne ćelije, pa `$` i `32,228` čine dva stupca od kojih je jedan bez značenja;
takve se ćelije spajaju s iznosom koji opisuju. Prazne ćelije razmaka ostaju netaknute, jer se
pojavljuju u različitom broju po retku, pa bi uklanjanje redak po redak pomaknulo vrijednosti pod
pogrešno zaglavlje.

Financijski izvještaji dohvaćaju se iz drugog izvora. EDGAR uz svaku objavu iz predanih XBRL podataka
generira i skup izvještaja, po jedan u zasebnoj datoteci (`R1.htm`, `R2.htm`, …), a njihov popis stoji
u indeksu `FilingSummary.xml`. To su financijski izvještaji već rekonstruirani u ispravne tablice, s
pravim zaglavljima, s mjernom jedinicom u naslovu izvještaja i s imenom pojma taksonomije na ćeliji
naziva svakog retka. Posljednje je najvrednije, jer povezuje dvosmislen naziv stavke s pojmom na koji
se oslanja provjera tagiranim podacima iz odjeljka 4.6. Item 8 čita te izvještaje, ostali Itemi glavni
dokument.

Ne preuzimaju se svi izvještaji. Jedna objava ih indeksira između sedamdeset i sto, a svaki je zaseban
zahtjev, pa se odabir provodi nad indeksom, prije ijednog preuzimanja. Uzimaju se svi izvještaji
kategorije `Statements`, jer nose ukupne iznose s kojima se sve ostalo usklađuje, i oni iz kategorije
`Details` čiji naslov sadrži ključnu riječ aktivnog čvora, jer ondje su razloženi prihod po proizvodu,
segmentu i regiji te trošak po segmentu. Ostale kategorije nose metapodatke, prazne predloške ili tekst
koji do modela već dolazi kroz Item 7. Izvještaji s oznakom `(Parenthetical)` i `Additional Information`
izuzimaju se za sve čvorove, jer nose samo pojedinačne podatke bez konteksta. Pravilo tako bira deset
do četrnaest izvještaja po objavi.

Dohvaćeni dokument i pripremljeni odsječci izvještaja spremaju se u međuspremnik na trideset minuta.
Jedan prolaz ekstrakcije više puta poseže za istim izvješćem, a bez međuspremnika svaki bi dohvat
značio novo preuzimanje i novu pripremu cijelog dokumenta.

## 4.4. Podjela dokumenta i odabir sadržaja

Godišnje izvješće prelazi sto stranica. Cijeli tekst ne stane u jedan poziv modela, a i kad bi stao,
točnost bi pala jer modeli slabije koriste podatke u sredini dugog konteksta [12]. Zato se dokument
dijeli, a zatim se bira koji se dijelovi uopće čitaju.

Podjela je dvostupanjska. Prvo se pronalaze granice Itema, zatim se unutar njih pronalaze podnaslovi.

Granice Itema traže se regexom, na dva načina. Prvi je oznaka Itema na početku retka; obrazac hvata i
slovo iza rednog broja (`Item 1A`) i decimalni oblik koji koristi obrazac 8-K (`Item 2.02`). Drugi je
propisani naslov stavke, jer dio izdavatelja u tijelu dokumenta ne ispisuje oznaku, nego samo naslov.
Naslovi su propisani regulativom, pa su jednako pouzdana oznaka granice kao i redni broj. Naslov se
priznaje samo ako zauzima cijeli redak, čime otpadaju spominjanja usred rečenice. Oznaka `Item 1` nema
naslovni oblik, jer je „Business" preopćenita riječ za pouzdano podudaranje.

```csharp
private static readonly (string Num, string Title)[] ItemTitles =
[
    ("1A", @"Risk\s+Factors"),
    ("7A", @"Quantitative\s+and\s+Qualitative\s+Disclosures?\s+About\s+Market\s+Risk"),
    ("7",  @"Management'?.?s\s+Discussion\s+and\s+Analysis(\s+of\s+Financial\s+Condition.*)?"),
    ("8",  @"Financial\s+Statements\s+and\s+Supplementary\s+Data"),
];

foreach (Match m in Regex.Matches(text, @"(?im)^[#>*_\s]*Item\s+(\d+(?:\.\d+)?[A-Z]?)\b"))
    found.Add((m.Groups[1].Value.ToUpperInvariant(), m.Index, m.Index + m.Length));

foreach (var (num, title) in ItemTitles)
    foreach (Match m in Regex.Matches(text, $@"(?im)^[#>*_\s]*{title}[.:\s]*$"))
        found.Add((num, m.Index, m.Index + m.Length));
```

Isječak programskog koda 4. Pronalaženje granica Itema po oznaci i po propisanom naslovu.

Jedna se oznaka pojavljuje više puta: u sadržaju na početku i na mjestu same stavke. Uzima se ono
pojavljivanje iza kojeg slijedi najviše teksta, pa redak iz sadržaja otpada sam od sebe, bez posebnog
pravila. Stavka završava na sljedećoj *različitoj* oznaci, jer izdavatelji naslov stavke ponavljaju kao
zaglavlje svake stranice, pa bi strože pravilo stavku prekinulo na njezinu prvom prijelomu stranice.

Ovdje je vidljiva uloga koja je rule-based metodama preostala. Regex u sustavu ne izvlači nijednu
vrijednost. Traži samo strukturu dokumenta, odnosno mjesta na kojima jedan Item prelazi u drugi. Oblik
oznake propisan je i stabilan, pa pravilo na njemu ne otkazuje. Vrijednosti unutar Itema nisu propisane
i njih preuzima model.

Podnaslovi unutar Itema prepoznaju se po retku čiji je cijeli tekst podebljan; redak s oznakom Itema
pritom je granica, a ne podnaslov. Jedno izvješće daje reda veličine stotinu podnaslova, a većina ne
nosi podatak koji aktivni čvor traži. Slanje svih odsječaka modelu bilo bi izvedivo, ali skupo.

Zato se uvodi korak trijaže. Modelu se šalju samo naslovi, numerirani, bez teksta ispod njih, a on
vraća redne brojeve podnaslova vrijednih čitanja. To je jedan poziv na brzoj razini modela opisanoj u
odjeljku 4.2, uz uključeno nametanje JSON izlaza.

```json
{"ids":[0,3,7,12]}
```

Isječak programskog koda 5. Odgovor modela u koraku trijaže.

Trijaža ne smije biti jedina brana. Ako poziv ne uspije ili model vrati prazan popis, sustav čita sve
podnaslove; otkaz trijaže tako poskupljuje obradu, ali je ne prekida. Uz to se Item 7 za čvorove
prihoda i troška uvijek dodaje u cijelosti, jer se njegov opis poslovanja po segmentima ne može
pouzdano ocijeniti po naslovu.

Dva slučaja zaobilaze podnaslove i čitaju se u cijelosti. Item 8 ne dijeli se iz dokumenta: njegovi
odsječci nastaju iz izvještaja opisanih u odjeljku 4.3, gdje je svaka datoteka već cjelovita
financijska tablica, a uzastopne tablice pakiraju se u odsječak uz naslov izvještaja kao oznaku izvora.
Item u kojem je pronađeno manje od pet podnaslova smatra se neprepoznatim, jer pravilo o podebljanju
otkazuje kod izdavatelja koji podnaslove ističu veličinom i bojom. Kad takav Item daje više odsječaka
nego što ih stane u proračun, odabir je determinističan i bez modela: boduju se ključne riječi aktivnog
čvora i prisutnost tablice, a odabrani odsječci prosljeđuju se redoslijedom u dokumentu. Odsječci s
tablicom imaju prednost jer tablice u izvješću nose iznose, a stoje pri kraju stavke, gdje bi ih
odsijecanje po redoslijedu izgubilo.

Odabrani sadržaj pakira se u odsječke. Granica odsječka je prazan redak, a odsječak se puni do četiri
tisuće znakova; granicu prelaze samo cjelovita tablica i tijelo pojedinog podnaslova koje je već samo
veće od nje. Uzastopni podnaslovi istog Itema spajaju se u jedan poziv, jer bi inače kratak podnaslov
trošio cijeli poziv modela, a odsječak nikada ne prelazi granicu Itema. Odlomak se ne prekida na pola:
onaj koji sam prelazi ograničenje reže se, osim ako je tablica. Tablica se prosljeđuje cijela, jer bi
rezanje izgubilo retke s iznosima, i uz sebe povlači uvodnu rečenicu. Rečenica koja tablicu najavljuje
zaseban je odlomak („The following table shows net sales by reportable segment … (dollars in
millions):"), a u njoj stoje predmet i mjerna skala; tablica koja bi bez nje pala na granicu odsječka
bila bi mreža golih brojeva.

```html
<table><tr><td colspan="3"></td><td colspan="15">Dec 27, 2025</td></tr>
<tr><td colspan="3">Year Ended ($ In Millions)</td>…<td colspan="3">CCG</td>…<td colspan="3">Total</td></tr>
<tr><td colspan="3">Revenue</td>…<td>$32,228</td>…<td>$49,147</td></tr>
<tr><td colspan="3">Operating income</td>…<td>$9,317</td>…<td>$12,739</td></tr></table>
```

Isječak programskog koda 6. Tablica poslovnih segmenata onako kako je prima agent-radnik.

Proračun jednog skeniranja iznosi četrdeset osam odsječaka po čvoru i izvješću. Troše ga tri izvora:
trijažirani podnaslovi, izvještaji Itema 8 i neprepoznati Itemi. Posljednji dobivaju ono što od
proračuna preostane, uz zajamčenih šest odsječaka po Itemu, jer je njihovo čitanje najmanje ciljano.

## 4.5. Paralelna ekstrakcija

Svaki odsječak čita jedan poziv modela. Pozivi su neovisni, pa se izvode paralelno, uz istovremeno
najviše šest poziva. Ograničenje postoji zbog ograničenja učestalosti zahtjeva prema pružatelju
usluge. Kao i trijaža, agenti-radnici rade na brzoj razini modela.

Prompt je specifikacija zadatka. Određuje što se traži, koja polja zapis ima, u kojem obliku se
vrijednost vraća i kada polje ostaje prazno. Traženi izlaz je JSON zadane sheme.

```json
{"sources":[{"name":"","classification":"","value":null,"percentage":null,
  "related_company":null,
  "proof":{"name":"","value":null,"percentage":null,
           "classification":null,"related_company":null}}]}
```

Isječak programskog koda 7. Shema odgovora agenta-radnika za čvor prihoda i troška.

Sva tri čvora vraćaju istu vanjsku strukturu. Razlikuju se u poljima. Prihod i trošak imaju iznos,
postotak i protustranku. Rizik nema iznos ni protustranku, nego kratku bilješku i opseg. Zbog
zajedničke vanjske strukture parsiranje odgovora dijeli se među čvorovima.

Prompt sadrži tri ograničenja koja proizlaze iz svojstava modela. Prvo, model smije vratiti samo ono
što je potkrijepljeno u zadanom odsječku, bez oslanjanja na vlastito predznanje o društvu. Time se
suzbija generiranje sadržaja koji u dokumentu ne postoji [16]. Drugo, klasifikacija mora biti točno
jedna vrijednost iz zadanog šifrarnika. Šifrarnici su `CUSTOMER`, `SEGMENT`, `REGION` i `PRODUCT` za
prihod, `COGS`, `OPEX` i `TOTAL_COSTS` za trošak te `MACROECONOMIC`, `INDUSTRY`, `BUSINESS`,
`LEGAL_REGULATORY`, `FINANCIAL` i `GENERAL` za rizik. Treće, za svako popunjeno polje model mora
vratiti doslovan isječak izvornog teksta iz kojeg je vrijednost preuzeta. Prazno polje nema isječak.

Zahtjev za doslovnim isječkom mijenja narav izlaza. Vrijednost više nije samo tvrdnja modela, nego
tvrdnja uz mjesto u dokumentu koje se može provjeriti. Time se ispunjava drugi od dvaju uvjeta
postavljenih u odjeljku 3.5.

Svi promptovi u sustavu su zero-shot. Prompt opisuje zadatak i propisuje oblik izlaza, ali ne sadrži
nijedan riješen primjer, dakle ni ulazni odsječak ni pripadajući odgovor. Prazna shema iz isječka 7
nije primjer u smislu few-shot pristupa [8], jer nije uparena s ulazom. Ona propisuje oblik, a ne
rješenje.

Izbor je namjeran, iz tri razloga. Prvo, cijena. Agent-radnik uz sistemski prompt prima odsječak od
četiri tisuće znakova; riješen primjer usporedive duljine povećao bi svaki od nekoliko desetaka
paralelnih poziva. Drugo, pristranost. Primjer bi morao biti uzet iz izvješća jednog izdavatelja, pa
bi model navodio na njegov rječnik i raspored tablica, a upravo je razlika među izdavateljima razlog
uvođenja jezičnog modela. Treće, održavanje. Primjer bi trebao postojati za svaki čvor i uz svaku
izmjenu sheme bi se morao provjeravati.

Zadatak koji se u few-shot pristupu obično rješava primjerom, a to je pogađanje oblika izlaza, ovdje
je riješen drukčije: nametanjem JSON izlaza iz odjeljka 4.2, ispisom sheme u promptu i tolerantnim
čitanjem odgovora.

Odgovori se objedinjuju. Kandidati iz svih odsječaka spajaju se u jedan popis, a duplikati se
uklanjaju po nazivu.

```csharp
var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
foreach (var list in perChunk)
    foreach (var s in list)
        if (!string.IsNullOrWhiteSpace(s.Name) && !byName.ContainsKey(s.Name))
            byName[s.Name] = s;
```

Isječak programskog koda 8. Objedinjavanje kandidata iz odsječaka.

Pravilo razrješavanja sukoba na ovoj razini je determinističko. Ako dva odsječka daju zapis istog
naziva, zadržava se prvi. Odsječci pristižu redoslijedom pripadnosti Itemima, pa prvi zapis dolazi iz
Itema koji je za taj čvor prioritetan. Pravilo je jednostavno i ponovljivo, ali ne
ocjenjuje koja je vrijednost točnija. Stvarna provjera vrijednosti ne događa se ovdje, nego u
sljedećem koraku, usporedbom s tagiranim podacima.

Otkaz jednog poziva ne ruši prolaz. Odsječak čiji je poziv pao vraća prazan popis, a ostali se
nastavljaju. Isto vrijedi za odgovor prekinut zbog ograničenja duljine izlaza: sustav pokušava
spasiti dio niza koji je zatvoren i odbacuje samo posljednji nepotpuni zapis.

## 4.6. Strukturirana provjera tagiranim podacima

Uz izvješće pisano za ljude, izdavatelj objavljuje i strojno čitljivu inačicu istih iznosa u formatu
XBRL. Iznosi su ondje označeni imenom pojma iz propisane taksonomije, razdobljem i mjernom jedinicom.
Sustav to koristi kao neovisan izvor brojčanih vrijednosti.

Načelo je podjela odgovornosti. Iznos dolazi iz tagiranog podatka. Naziv stavke, segment i
protustranka dolaze iz teksta koji je pročitao model. Model ne prepisuje velike iznose, jer za to
postoji točniji izvor.

Podaci se čitaju iz dva izvora. Prvi su ukupni iznosi na razini društva, dostupni preko zbirnog
sučelja. Različiti izdavatelji isti podatak tagiraju različitim pojmom, pa se pojmovi pokušavaju
redom.

```csharp
public static readonly string[] Cogs =
    ["CostOfRevenue", "CostOfGoodsAndServicesSold", "CostsAndExpenses"];
public static readonly string[] Opex =
    ["OperatingExpenses", "SellingGeneralAndAdministrativeExpense"];
public static readonly string[] Revenue =
    ["Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax",
     "RevenueFromContractWithCustomerIncludingAssessedTax"];
```

Isječak programskog koda 9. Pojmovi taksonomije, poredani po prednosti.

Bira se godišnji podatak čiji se kraj razdoblja podudara s datumom izvještaja obrađivane objave, a ne
najnoviji podatak u zbirci. Bez tog uvjeta sustav bi uz izvješće iz ranije godine prikazao iznos iz
posljednje objavljene godine.

Drugi izvor je instancni XBRL dokument same objave. Zbirno sučelje ne nosi dimenzije, pa iz njega
nije moguće dobiti iznose po poslovnim segmentima. Instancni dokument nosi. Iz njega se za svaki
segment čitaju tagirani prihod i tagirana operativna dobit, povezani zajedničkim kontekstom, iz čega
se izvodi implicirani trošak segmenta.

```csharp
public record SegmentCost(string Segment, double Revenue, double OperatingIncome,
                          double Cost, bool Reconciles);
```

Isječak programskog koda 10. Zapis troška jednog poslovnog segmenta.

Polje `Reconciles` nosi rezultat provjere. Implicirani trošak mora biti nenegativan i ne veći od
prihoda segmenta. Ako uvjet ne vrijedi, spojena je pogrešna mjera dobiti i podatak se označava
nepouzdanim. Uz to se provodi provjera zbroja: zbroj prihoda po segmentima uspoređuje se s ukupnim
prihodom društva, uz dopušteno odstupanje od jedan posto.

Obje provjere izvode se u kodu, ne u promptu. Riječ je o aritmetici, a aritmetika je zadatak u kojem
jezični model nema prednost.

Provjere ne odbacuju podatak. Neusklađenost se označava i prosljeđuje dalje. Tiho odbacivanje krilo
bi činjenicu da izvori ne daju isti odgovor.

Za čvor rizika ovaj korak ne postoji. Objavljeni rizik nema brojčanu vrijednost, pa nema ni tagiranog
podatka s kojim bi se usporedio.

## 4.7. Objedinjavanje i priprema zapisa

Nalazi agenata-radnika oblikuju se u sažetak. Sažetak sadrži naziv, klasifikaciju, iznos, postotak i
protustranku svakog kandidata, oznaku Itema iz kojeg potječe te doslovne isječke po poljima.

Sažetak i tagirane činjenice zajedno čine utemeljenje za posljednji poziv. Taj poziv obavlja vodeći
agent, kojem se dodjeljuje podrazumijevana, snažnija razina modela iz odjeljka 4.2. Njegov zadatak
nije ponovno čitanje izvješća. Zadatak je spajanje dvaju izvora i priprema zapisa.

Prompt vodećeg agenta određuje redoslijed prednosti. Za iznos ima prednost tagirani podatak. Za
naziv, segment i protustranku ima prednost tekst koji su pročitali agenti-radnici. Ako se iznos iz
teksta razlikuje od tagiranog iznosa za istu stavku, agent to mora navesti, a ne tiho odabrati jedan
od njih.

Time se zatvara pitanje sukoba iz odjeljka 3.3. Sustav sukob rješava na dvije razine. Sukob između
odsječaka rješava se determinističkim pravilom prvenstva. Sukob između pročitane vrijednosti i
tagirane vrijednosti ne rješava se automatski, nego se označava i prepušta korisniku.

Zapis se ne upisuje sam. Kada korisnik potvrdi stavku, agent ispisuje blok za pohranu zadane sheme.

```json
{"name":"","classification":"COGS","value":null,"percentage":null,
 "related_company":null,"related_company_ticker":null,"reference":null,
 "proof":{"name":"","value":null,"percentage":null,
          "classification":null,"related_company":null}}
```

Isječak programskog koda 11. Blok za pohranu zapisa o trošku.

Polje `reference` sadrži doslovan odlomak iz kojeg je cijeli zapis izveden, uz naznaku Itema.
Polja unutar `proof` sadrže isječak po pojedinom polju. Razlika je namjerna. Jedan zapis ima jedan
izvorni odlomak, ali svako njegovo polje može biti potkrijepljeno drugom rečenicom.

## 4.8. Entitetski model

Ekstrahirani podaci pohranjuju se u tri entiteta izvora, po jedan za svaki čvor. Uz njih stoje
entitet objave i entitet dokaza po polju.

| Entitet | Sadržaj |
|---|---|
| `RevenueSource` | izvor prihoda: naziv, tip, iznos, postotak, protustranka, izvorni odlomak |
| `CostSource` | izvor troška: naziv, osnovica, iznos, postotak, protustranka, izvorni odlomak |
| `CompanyRisk` | objavljeni rizik: naziv, opseg, bilješka, izvorni odlomak |
| `Filing` | objava u sustavu EDGAR: oznaka objave, obrazac, datum, poveznica na dokument |
| `SourceFieldReview` | dokaz za jedno polje jednog zapisa |

Tablica 3. Entiteti u koje se ekstrahirani podaci pohranjuju.

Objava se pohranjuje jednom i povezuje sa svakim zapisom koji se na nju poziva. Identitet objave je
njezina oznaka u sustavu EDGAR, koja je jedinstvena.

Entitet dokaza nosi vezu na zapis, oznaku polja, doslovan isječak i vezu na objavu iz koje isječak
potječe.

```csharp
public class SourceFieldReview
{
    public long Id { get; set; }
    public long CompanyId { get; set; }
    public long? RevenueSourceId { get; set; }
    public long? CostSourceId { get; set; }
    public long? CompanyRiskId { get; set; }
    public ReviewableField Field { get; set; }
    public string ReferenceSnapshot { get; set; } = string.Empty;
    public string? ReferencedValue { get; set; }
    public long? FilingId { get; set; }
}
```

Isječak programskog koda 12. Entitet dokaza za jedno polje.

Veza na objavu stoji na dokazu, a ne na zapisu. Razlog je granularnost. Jedan izvor prihoda može
imati iznos potkrijepljen jednom objavom i udio potkrijepljen drugom. Da veza stoji na zapisu, taj
slučaj se ne bi mogao prikazati.

Polje `ReviewableField` nabraja polja koja se mogu potkrijepiti: `VALUE`, `PERCENTAGE`, `NAME`,
`RELATED_COMPANY`, `CLASSIFICATION` i `NOTE`. Po zapisu i polju postoji najviše jedan važeći dokaz.
Novi dokaz zamjenjuje prethodni.

## 4.9. Normalizacija vrijednosti

U odjeljku 2.2 razdvojeno je pronalaženje podatka od svođenja vrijednosti na oblik ciljne sheme. U
sustavu se ta dva zadatka događaju na različitim mjestima.

Mjerna skala i valuta normaliziraju se u promptu. Financijske tablice iznose prikazuju u tisućama ili
milijunima, uz napomenu iznad tablice. Model tu napomenu vidi zajedno s brojem, pa mu se nalaže da
vrati apsolutni iznos u dolarima. Postotak se traži u rasponu od nula do sto. Podatak koji nije
naveden vraća se kao prazan, a ne kao nula.

Tipizacija i provjera pripadnosti šifrarniku ostaju u kodu. Vrijednost klasifikacije koju je model
vratio pokušava se pretvoriti u pripadajući nabrojeni tip. Neuspjeh znači da se zapis ne pohranjuje.

```csharp
private long? UpsertRevenue(long companyId, long? rowId, string classification, string name,
    double? value, double? percentage, long? relatedCompanyId, string? reference, Contributor by)
{
    if (!Enum.TryParse<SourceType>(classification, out var sourceType)) return null;
    ...
}
```

Isječak programskog koda 13. Provjera klasifikacije prije pohrane.

Podjela slijedi iz prirode zadataka. Skala i valuta ovise o kontekstu u dokumentu, koji vidi samo
model. Pripadnost šifrarniku ne ovisi o dokumentu i mora vrijediti bezuvjetno, pa se ne prepušta
modelu. Prompt traži vrijednost iz šifrarnika, ali ta se tvrdnja ne uzima na riječ.

Brojčane vrijednosti čitaju se tolerantno. Model iznos može vratiti kao broj ili kao niz znakova, a
oba oblika se prihvaćaju.

## 4.10. Postupanje s pogreškama

Jezični model je nedeterministična komponenta. Uz to sustav ovisi o vanjskim uslugama koje mogu biti
nedostupne. Svaka takva točka ima definirano ponašanje pri otkazu.

| Točka otkaza | Ponašanje |
|---|---|
| indeks izvještaja objave nedostupan | Item 8 se čita sekvencijalno iz glavnog dokumenta |
| dohvat pojedinog izvještaja ne uspije | taj se izvještaj preskače, ostali se obrađuju |
| Item daje manje od pet podnaslova | Item se čita sekvencijalno, bez trijaže |
| trijaža ne uspije ili vrati prazan popis | čitaju se svi podnaslovi |
| poziv agenta-radnika ne uspije | odsječak daje prazan popis, prolaz se nastavlja |
| odgovor prekinut zbog duljine | spašavaju se zatvoreni zapisi, zadnji nepotpuni se odbacuje |
| instancni XBRL dokument nedostupan | izostaju iznosi po segmentima, ukupni iznosi ostaju |
| klasifikacija izvan šifrarnika | zapis se ne pohranjuje |

Tablica 4. Ponašanje sustava pri otkazu pojedine komponente.

Zajedničko načelo je da otkaz jedne komponente smanjuje količinu podataka, a ne prekida obradu.
Iznimka je posljednji redak. Zapis koji ne prolazi provjeru šifrarnika ne pohranjuje se, jer bi
neispravna klasifikacija ušla u bazu.

## 4.11. Proširivost

Dodavanje novog čvora ekstrakcije zahtijeva izmjene na četiri mjesta: popis Itema koji se čitaju,
prompt agenta-radnika i vodećeg agenta, šifrarnik klasifikacije i ciljni entitet. Motor obrade se ne
mijenja. Dohvat, podjela dokumenta, trijaža, paralelno izvođenje, objedinjavanje i pohrana dokaza rade
jednako za svaki čvor.

To je razlika prema rule-based pristupu opisanom u odjeljku 2.3. Ondje novi tip podatka znači nova
pravila za prepoznavanje, a pravila su vezana uz oblik dokumenta pojedinog izdavatelja. Ovdje novi
tip podatka znači novi opis zadatka, koji nije vezan uz oblik dokumenta.

---

## Napomene za uređivanje

Ovaj dio ne ulazi u rad.

1. **Rečenica razgraničenja u uvodu.** Postojeća formulacija „nema dohvata s interneta" nije točna.
   Sustav dohvaća dokument, zbirno XBRL sučelje i instancni dokument preko mreže. Predložena zamjena
   nalazi se u drugom odlomku ovog poglavlja: sustav radi nad jednim dokumentom i njegovim
   strukturiranim prilogom, a web ne pretražuje kao izvor podataka za ekstrakciju. Ista rečenica
   pokriva i pomoćnu pretragu protustranaka, bez posebnog objašnjenja.

2. **Numeriranje literature.** Poglavlje koristi tri već potvrđene reference: [12] u odjeljku 4.4 te
   [8] i [16] u odjeljku 4.5. Numeracija iz poglavlja 2 i 3 ostaje netaknuta.

3. **Numeriranje isječaka i tablica.** Isječci programskog koda numerirani su od 1, tablice također.
   Ako prethodna poglavlja sadrže isječke ili tablice, brojeve treba pomaknuti.

4. **Slika 1.** Dijagram tijeka obrade nacrtan je znakovima. Za predaju ga treba prerisati kao sliku.

5. **XBRL bez najave.** Prema dogovoru, XBRL se uvodi tek ovdje. Ako se pri čitanju cjeline pokaže
   prenaglo, najmanji zahvat je jedna rečenica u odjeljku 2.1, gdje se ionako gradi podjela podataka
   prema stupnju strukture.

6. **Poveznica prema 5. poglavlju.** Tagirane XBRL činjenice su brojčane vrijednosti dobivene bez
   jezičnog modela. Za polje iznosa mogu poslužiti kao referentna vrijednost pri mjerenju preciznosti
   i odziva, čime otpada potreba za ručnim označavanjem skupa dokumenata. Vrijedi samo za čvorove
   prihoda i troška. Čvor rizika nema brojčanu vrijednost i mora se vrednovati drukčije.

7. **Trošak održavanja.** Odjeljak 4.11 daje kvalitativan argument za drugo istraživačko pitanje:
   novi tip podatka ne zahtijeva nova pravila prepoznavanja. To je najbliže mjerljivom obliku
   kriterija fleksibilnosti. Kriterij troška održavanja i dalje nije riješen.

8. **Nazivi u upotrebi.** odsječak (chunk), podnaslov, trijaža, agent-radnik, vodeći agent, sažetak
   nalaza, utemeljenje, blok za pohranu, tagirani podaci, instancni dokument, čvor ekstrakcije.

9. **Nazivi modela zastarijevaju.** Isječak 3 navodi konkretne nazive modela, koji su točni na dan
   izrade sustava. U radu uz tablicu ili isječak treba stajati datum, na primjer „modeli dostupni u
   lipnju 2026." Bez datuma isječak izgleda netočno već pri obrani.

10. **Prostor za 5. poglavlje.** Odjeljak 4.2 opisuje sloj koji dopušta zamjenu modela bez izmjene
    ekstrakcije. To znači da se isto izvješće može obraditi s više modela i usporediti rezultat. Ako
    se u vrednovanju odlučiš na usporedbu pružatelja usluge, arhitektura to već podupire i ne traži
    izmjene koda. Isto vrijedi za usporedbu zero-shot i few-shot prompta: dodavanje riješenog
    primjera u prompt agenta-radnika je izmjena jednog niza znakova, pa je i to izvediv pokus ako
    zatreba brojka uz tvrdnju iz odjeljka 4.5.

11. **Ako se 3. poglavlje dopunjava.** Odjeljak 3.2 obrađuje prompt kao specifikaciju zadatka. Ako
    ondje zero-shot i few-shot nisu razgraničeni, tvrdnja u 4.5 visi u zraku i traži jednu rečenicu
    definicije u 3.2, uz istu referencu [8].

12. **Item kao naziv.** Numerirani dijelovi obrasca 10-K zadržavaju izvornu oznaku (`Item 7`,
   `Item 8`), a ne prevode se u „poglavlje" ni „stavku". Razlog je dvostruk: „poglavlje" se sudara s
   poglavljima rada, a „stavka" sa stavkama financijskih izvještaja, o kojima poglavlje govori. Uz to
   je `Item` doslovna oznaka koju sustav traži u tekstu dokumenta, pa se naziv u radu i niz u regexu
   podudaraju. Prvo spominjanje uvodi obrazac i oznaku, uz tablicu 1.
