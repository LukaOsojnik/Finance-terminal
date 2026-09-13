# X. Mjerenje ekstrakcije protustranaka

Mjerenje ekstrakcije podijeljeno je u dva dijela. U prvom dijelu prikazuje se rezultat uobičajene interakcije korisnika s AI chatom. U drugom dijelu komponenta *Measurement consumer* ponavlja ekstrakciju istog izvješća radi mjerenja ponovljivosti rezultata. Oba dijela provode se s blažim i sa strožim promptom za agente-radnike. Blaži prompt dopušta šire prepoznavanje mogućih protustranaka, dok stroži prompt zahtijeva da tekst izvješća izričito navodi kupnju, prodaju, opskrbu, licenciranje, distribuciju ili konkretnu poslovnu suradnju.

U oba načina upotrebljavaju se isto izvješće, isti model i jednake postavke procesa ekstrakcije. Jedina je promjenjiva strogost prompta agenata-radnika. Proces se sastoji od agenata-radnika, koji analiziraju pojedine dijelove izvješća i izdvajaju protustranke s pripadajućim evidenceom, te vodećeg agenta, koji objedinjuje njihove nalaze i oblikuje konačan rezultat.

Za svaki se prompt u komponenti *Measurement consumer* provodi deset ekstrakcija. Prvo pokretanje priprema predmemoriju deterministički dobivenih naslova i odjeljaka izvješća, ali se njegov rezultat uključuje u mjerenje. Predmemorirani sadržaj nije rezultat rada jezičnog modela, dok svako pokretanje izvodi vlastite pozive agenata-radnika i vodećeg agenta. Zbog toga se svih deset LLM ekstrakcija unutar pojedinog načina smatra neovisnima.

## Metodologija mjerenja

Mjerenje je provedeno s blažom i strožom inačicom prompta za agente-radnike. Blaža inačica sadržavala je osnovne upute za izdvajanje protustranaka, dok je stroža inačica dodatno propisivala uvjete koje odnos mora ispuniti kako bi se kompanija smjela izdvojiti kao protustranka. U oba je slučaja korišten jednak prompt vodećeg agenta. Jedina razlika između dvaju mjerenja stoga je bila inačica prompta agenata-radnika. Cjeloviti tekstovi blažeg i strožeg prompta agenata-radnika te zajedničkog prompta vodećeg agenta prikazani su u Prilogu A.

Mjerenje je provedeno sa sljedećim postavkama:

| Postavka | Vrijednost |
|---|---|
| Kompanija | Super Micro Computer, Inc. |
| Vrsta izvješća | Form 10-K |
| Razdoblje izvješća | Godina završena 30. lipnja 2025. |
| Datum prijave | 28. kolovoza 2025. |
| SEC pristupni broj | `0001375365-25-000027` |
| Jezični modeli | `deepseek-v4-flash` za agente-radnike; `deepseek-v4-pro` za vodećeg agenta |
| Pružatelj modela | DeepSeek |
| Datum mjerenja | 27. kolovoza 2026. |
| Broj pokretanja | 10 po inačici prompta; ukupno 20 |
| Temperatura | Nije postavljena; primjenjuje se zadana vrijednost pružatelja, uz uključen način razmišljanja |
| Najveći broj izlaznih tokena | 16.000 za agente-radnike i vodećeg agenta; ponovljeni poziv agenta-radnika nakon nepotpunog odgovora dopušta do 32.000 tokena |

Glavna kvantitativna metrika jest **ponovljivost ekstrakcije**, a mjeri se Jaccardovim indeksom. Za svako pokretanje konačan rezultat vodećeg agenta promatra se kao skup izdvojenih protustranaka. Prije usporedbe nazivi se normaliziraju kako razlike u velikim i malim slovima, interpunkciji i uobičajenim pravnim nastavcima naziva kompanija ne bi bile pogrešno protumačene kao različite protustranke.

Pri normalizaciji se spajaju i poznate varijante naziva iste kompanije. U rezultatima mjerenja `AMD` se stoga izjednačava s nazivom `Advanced Micro Devices, Inc.`, `Leadtek` s nazivom `Leadtek Research Inc.`, a `Mega Bank` s nazivom `Mega International Commercial Bank`. Izvorni nazivi ostaju sačuvani radi sljedivosti, dok se Jaccardov indeks računa nad njihovim kanonskim nazivima.

Za dva skupa protustranaka \(A\) i \(B\) Jaccardov indeks definiran je izrazom:

\[
J(A,B)=\frac{|A \cap B|}{|A \cup B|}
\]

Brojnik predstavlja broj protustranaka zajedničkih obama pokretanjima, a nazivnik ukupan broj različitih protustranaka pronađenih u barem jednom od njih. Vrijednost indeksa nalazi se između 0 i 1. Vrijednost 1 označuje potpuno jednake skupove rezultata, dok vrijednost 0 znači da uspoređena pokretanja nemaju nijednu zajedničku protustranku.

Deset pokretanja istog prompta tvori 45 jedinstvenih parova. Jaccardov indeks izračunava se za svaki par, nakon čega se kao konačna mjera ponovljivosti navodi njihov aritmetički prosjek:

\[
\overline{J}=\frac{1}{45}\sum_{i=1}^{9}\sum_{j=i+1}^{10}J(S_i,S_j)
\]

gdje \(S_i\) označuje skup protustranaka dobiven u \(i\)-tom pokretanju. Izračun se provodi zasebno za blaži i zasebno za stroži prompt. Radi lakšeg tumačenja rezultat će se prikazati i kao postotak. Primjerice, prosječni Jaccardov indeks od 0,80 odgovara prosječnom podudaranju skupova od 80 %. Na kraju će se usporediti ponovljivost i broj izdvojenih protustranaka između dvaju promptova.

Uz prosječni Jaccardov indeks prikazat će se najmanja i najveća dobivena vrijednost te frekvencija pojavljivanja svake protustranke, primjerice 10/10 ili 6/10. Te vrijednosti nisu zasebne glavne metrike, nego služe objašnjenju uočenih razlika između pokretanja.

Uz ponovljivost bilježit će se i postoji li evidence uz svaku izdvojenu protustranku. Neće se procjenjivati njegov sadržaj ni točnost, nego samo je li ga model uključio u rezultat. Postojanje evidencea prikazat će se oznakom *da* ili *ne*.

## X.1. Mjerenje AI chata

U prvom dijelu korisnik putem AI chata zadaje zahtjev za ekstrakciju protustranaka iz odabranog financijskog izvješća. Izvješće se najprije obrađuje jedanput s blažim, a zatim jedanput sa strožim promptom. Time se prikazuje rezultat koji bi korisnik dobio tijekom pojedinačne uporabe svakog načina ekstrakcije.

Iz svakog dobivenog odgovora bilježe se:

- ukupan broj izdvojenih protustranaka;
- popis izdvojenih protustranaka;
- postoji li evidence uz svaku protustranku.

Jaccardov indeks u ovom se dijelu ne izračunava jer se za svaki prompt dobiva samo jedan skup rezultata. Rezultati blažeg i strožeg prompta usporedit će se prema ukupnom broju i popisu izdvojenih protustranaka te postojanju evidencea.

### Rezultat mjerenja AI chata

Rezultati pojedinačne ekstrakcije putem AI chata prikazani su u Tablici X. Mjerenje je provedeno jedanput s blažim i jedanput sa strožim promptom nad istim financijskim izvješćem.

| Prompt | Broj izdvojenih protustranaka | Protustranke s evidenceom | Protustranke bez evidencea |
|---|---:|---:|---:|
| Blaži | 15 | 15 | 0 |
| Stroži | 9 | 9 | 0 |

**Tablica X.** Usporedba rezultata ekstrakcije putem AI chata.

Blaži prompt izdvojio je 15 protustranaka, dok je stroži prompt izdvojio njih 9. Svih devet protustranaka dobivenih strožim promptom pojavilo se i u rezultatu blažeg prompta. Blaži prompt dodatno je izdvojio Broadcom, Samsung Electronics, Micron Technology, Mega International Commercial Bank, Yuanta Commercial Bank i U.S. Bank Trust Company. Evidence je postojao uz svaku izdvojenu protustranku u obama pokretanjima. U skladu s metodologijom mjereno je samo postojanje evidencea, a ne njegova sadržajna točnost.

#### Blaži prompt

Primjenom blažeg prompta AI chat izdvojio je ukupno **15 jedinstvenih protustranaka** nakon spajanja očitih varijanti njihovih naziva. Dobiveni rezultat prikazan je u sljedećoj tablici.

| Protustranka | Odjeljak izvješća | Evidence postoji |
|---|---|---|
| NVIDIA / NVIDIA Corporation | Item 1 i Item 7 | Da |
| Intel / Intel Corporation | Item 1 i Item 7 | Da |
| AMD / Advanced Micro Devices, Inc. | Item 1 i Item 7 | Da |
| Broadcom Inc. | Item 7 | Da |
| Samsung Electronics Company Limited | Item 7 | Da |
| Micron Technology, Inc. | Item 7 | Da |
| Ablecom Technology, Inc. / Ablecom | Item 1 i Item 7 | Da |
| Compuware Technology, Inc. / Compuware | Item 1 i Item 7 | Da |
| BDO USA, P.C. | Item 8 | Da |
| Deloitte & Touche LLP | Item 8 | Da |
| Mega International Commercial Bank | Item 8 | Da |
| Yuanta Commercial Bank Co., Ltd. | Item 8 | Da |
| U.S. Bank Trust Company, National Association | Item 8 | Da |
| Leadtek | Item 8 | Da |
| Green Earth Liang’s Inc. | Item 8 | Da |

Provjerom rezultata utvrđeno je da svih 15 izdvojenih protustranaka ima pripadajući evidence. U skladu s definiranom metodologijom provjeravano je samo njegovo postojanje, a ne sadržaj ili točnost.

#### Stroži prompt

Primjenom strožeg prompta AI chat izdvojio je ukupno **9 jedinstvenih protustranaka** nakon spajanja varijanti njihovih naziva.

| Protustranka | Odjeljak izvješća | Evidence postoji |
|---|---|---|
| NVIDIA | Item 1 | Da |
| Intel | Item 1 | Da |
| AMD | Item 1 | Da |
| Ablecom Technology, Inc. / Ablecom | Item 1 i Item 7 | Da |
| Compuware Technology, Inc. / Compuware | Item 1 i Item 7 | Da |
| Leadtek Research Inc. / Leadtek | Item 8 | Da |
| Green Earth Liang’s Inc. / Green Earth | Item 8 | Da |
| BDO USA, P.C. | Item 8 | Da |
| Deloitte & Touche LLP | Item 8 | Da |

Provjerom rezultata utvrđeno je da svih 9 izdvojenih protustranaka ima pripadajući evidence. NVIDIA, Intel i AMD potkrijepljeni su zajedničkim isječkom u kojem se navode sve tri kompanije. U skladu s metodologijom provjeravano je samo postojanje evidencea, a ne njegov sadržaj ili točnost.

## X.2. Mjerenje komponente Measurement consumer

U drugom dijelu komponenta *Measurement consumer* pokreće deset neovisnih ekstrakcija s blažim i deset sa strožim promptom nad istim financijskim izvješćem. Svako pokretanje uključuje vlastito skeniranje agenata-radnika i vlastiti poziv vodećem agentu. Prvo pokretanje svake skupine izvodi se prije ostalih, dok se preostalih devet pokretanja može izvršavati paralelno. Svih deset rezultata pojedinog prompta ravnopravno sudjeluje u njegovu izračunu Jaccardova indeksa.

Za svako pokretanje bilježe se ukupan broj protustranaka koje su izdvojili agenti-radnici, ukupan broj protustranaka u konačnom rezultatu vodećeg agenta te eventualne greške. Jaccardov indeks izračunava se nad skupovima protustranaka iz konačnih rezultata vodećeg agenta jer oni predstavljaju izlaz procesa koji se prikazuje korisniku. Rezultati dvaju promptova obrađuju se odvojeno, a njihove konačne vrijednosti zatim se uspoređuju.

### Rezultat mjerenja komponente Measurement consumer

Za svaki prompt uspoređeno je svih 45 jedinstvenih parova dobivenih iz deset pokretanja. Prije izračuna primijenjena je definirana normalizacija naziva, uključujući spajanje varijanti poput `AMD` i `Advanced Micro Devices, Inc.` te `Leadtek` i `Leadtek Research Inc.`. Jaccardov indeks izračunat je nad konačnim skupovima protustranaka vodećeg agenta.

| Prompt | Prosječni Jaccardov indeks | Najmanja sličnost | Parovi s najmanjom sličnošću | Najveća sličnost | Parovi s najvećom sličnošću | Evidence |
|---|---:|---:|---|---:|---|---|
| Blaži | 0,711 (71,14 %) | 0,500 | 3–6, 4–6 i 6–8 | 1,000 | 2–10 | Prisutan uz sve rezultate |
| Stroži | 0,671 (67,08 %) | 0,429 | 5–6 | 1,000 | 1–10, 2–3, 2–4 i 3–4 | Prisutan uz sve rezultate |

**Tablica X.** Ponovljivost ekstrakcije izmjerena Jaccardovim indeksom.

Blaži prompt ostvario je prosječni Jaccardov indeks od 0,711, što znači da su se konačni skupovi protustranaka iz dvaju pokretanja prosječno podudarali 71,14 %. Najmanja zabilježena sličnost iznosila je 0,500 i pojavila se u trima parovima, dok su pokretanja 2 i 10 proizvela jednake skupove protustranaka.

Stroži prompt ostvario je nešto niži prosječni Jaccardov indeks od 0,671, odnosno prosječno podudaranje od 67,08 %. Najveća razlika zabilježena je između pokretanja 5 i 6, čiji je Jaccardov indeks iznosio 0,429. Potpuno jednaki skupovi dobiveni su u četirima parovima pokretanja.

Evidence je bio prisutan uz svaku izdvojenu protustranku u svih deset pokretanja blažeg i svih deset pokretanja strožeg prompta. Nije zabilježena nijedna stavka bez evidencea. U skladu s metodologijom provjeravano je samo njegovo postojanje, a ne sadržajna točnost.

### Zaključak mjerenja

Rezultati pokazuju da je blaži prompt u ovom mjerenju bio nešto ponovljiviji od strožeg prompta, s razlikom prosječnoga Jaccardova indeksa od približno 0,041, odnosno 4,06 postotnih bodova. Takav rezultat ima smisla jer je modelu jednostavnije dosljedno izdvojiti širi skup spomenutih kompanija nego za svaku kompaniju dodatno procijeniti potvrđuje li tekst izričito da je ona protustranka analizirane kompanije. Stroži prompt uvodi dodatni korak prosudbe, pa granični slučajevi mogu biti različito protumačeni u pojedinim pokretanjima. To predstavlja moguće objašnjenje rezultata, a ne dokazani uzročni odnos. Ipak, nijedan prompt nije proizveo potpuno stabilan rezultat kroz svih deset pokretanja. U oba mjerenja vodeći je agent u dvama pokretanjima izdvojio izraz `Corporate Venture` kao protustranku, iako on nije naziv kompanije. To pokazuje da ponovljivost ne predstavlja nužno i točnost ekstrakcije. U sirovom prikazu uočene su i razlike poput `Leadtek` i `Leadtek Research Inc.` te `AMD` i `Advanced Micro Devices, Inc.`; njihovim spajanjem spriječeno je da različito zapisivanje iste kompanije neopravdano smanji Jaccardov indeks.

### Ograničenja mjerenja

Mjerenje je provedeno nad samo jednim financijskim izvješćem, uporabom jedne kombinacije modela istog pružatelja i ukupno 20 ponavljanja, odnosno deset za svaki prompt. Zbog toga se dobiveni rezultati ne mogu izravno generalizirati na druga izvješća, kompanije, pružatelje ili modele. Također nije unaprijed izrađen referentni popis svih stvarnih protustranaka u izvješću. Stoga se mjerenjem može ocijeniti ponovljivost ekstrakcije, ali ne i njezina potpuna točnost, preciznost ili odziv.

# Prilog A — Promptovi korišteni u mjerenju

## A.1. Blaži prompt agenata-radnika

```text
You extract NAMED COUNTERPARTIES from one plain-text excerpt of a US public company's 
SEC filing. Find only COST-SIDE counterparties: named suppliers, vendors, manufacturers, 
foundries, contract producers, licensors, or service providers from which the filer buys 
goods, rights, or services. A counterparty must be a named company and the excerpt must 
establish an explicit commercial relationship. Do not use outside knowledge. Do not return 
business segments, products, regions, industries, unnamed customer or supplier concentrations, 
competitors, litigation adversaries, acquisition targets, or companies merely mentioned without 
the required relationship. Do not derive values or percentages from financial tables or 
company-wide figures. A relationship with no stated amount is valid. For every result, 
write evidence first as one verbatim substring that names the counterparty and establishes 
the relationship. Then return name exactly as written, related_company as the same name 
and a short note describing the relationship. 
Reply with JSON only: {"sources":[{"evidence":"","name":"","related_company":"","note":""}]}. 
If the excerpt establishes no matching counterparty, reply {"sources":[]}.
```

## A.2. Stroži prompt agenata-radnika

```text
You extract NAMED COUNTERPARTIES from one plain-text excerpt of a US public company's 
SEC filing. Find only COST-SIDE counterparties: named suppliers, vendors, manufacturers, 
foundries, contract producers, licensors, or service providers from which the filer buys 
goods, rights, or services. A counterparty must be a named company and the excerpt must 
establish an explicit commercial relationship. Do not use outside knowledge. Do not 
return business segments, products, regions, industries, unnamed customer or supplier 
concentrations, competitors, litigation adversaries, acquisition targets, or companies 
merely mentioned without the required relationship. Do not derive values or percentages 
from financial tables or company-wide figures. A relationship with no stated amount is 
valid. For every result, write evidence first as one verbatim substring that names the 
counterparty and establishes the relationship. Then return name exactly as written, 
related_company as the same name and a short note describing the relationship. Reply 
with JSON only: {"sources":[{"evidence":"","name":"","related_company":"","note":""}]}. 
If the excerpt establishes no matching counterparty, reply {"sources":[]}. STRICT MODE: 
require the excerpt itself to state the purchase, sale, supply, license, distribution, 
resale, or concrete collaboration. A product mention, compatibility statement, industry 
list, or description of a company as a market leader is insufficient. If uncertain, omit it.
```

## A.3. Zajednički prompt vodećeg agenta

Sistemski prompt:

````text
You are the lead financial analyst for a repeatable filing-extraction measurement. 
Parallel workers have scanned one SEC filing and the filing context below contains 
their counterparty findings. Use only that context; do not use outside knowledge and 
never introduce a company absent from the findings. reply with NOTHING but a fenced block:
```ledger
{"items":[{"evidence":"","counterparty":"","direction":"SUPPLIER","what":"","section":""}]}
```
One item per NAMED counterparty in the findings above — every one of them, not a selection. 
evidence is the VERBATIM quote from the findings naming that counterparty 
(copy it exactly; do not paraphrase, do not shorten mid-word). counterparty 
is the company name. direction is exactly one of SUPPLIER, CUSTOMER, PARTNER. 
what is a SHORT description of what is bought or sold. section is the SEC Item 
the finding came from. Never include a company that is not in the findings above. 
Return exactly one item per counterparty. If the same counterparty appears more 
than once or under minor variations of the same name, merge those findings into 
one item and keep the clearest verbatim evidence. No prose before or after the block.
````

Korisnički prompt:

```text
Emit the full counterparty ledger for this filing.
```
