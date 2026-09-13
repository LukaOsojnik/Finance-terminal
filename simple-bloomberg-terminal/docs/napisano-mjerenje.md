	Mjerenje ekstrakcije
Mjerenje ekstrakcije je podijeljeno u dva dijela. Prvi dio prikazuje rezultat jedne uobičajene interakcije korisnika s LLM-om tijekom koje se odabrano financijsko izvješće obrađuje jedanput. U drugom dijelu measurement consumer deset puta za svaku inačicu prompta pokreće ekstrakciju nad istim izvješćem kako bi se ispitala ponovljivost rezultata.
U oba se dijela upotrebljavaju isto izvješće, ista kombinacija modela i jednake postavke procesa. Uspoređivane se varijante razlikuju samo u promptu agenata-radnika, dok prompt vodećeg agenta ostaje jednak. Agenti-radnici analiziraju pojedine dijelove izvješća te izdvajaju protustranke s pripadajućim poljem ,,Evidence’’, koje sadrži dokazni isječak iz izvornog teksta. Vodeći agent zatim objedinjuje njihove nalaze i oblikuje konačan rezultat.
Metodologija mjerenja
Ponovljivost ekstrakcije mjeri se Jaccardovim indeksom. Uspoređuju se blaža i stroža inačica prompta, koje se razlikuju samo po broju i preciznosti uputa. Blaža inačica daje modelu više slobode, dok stroža jasnije određuje uvjete pod kojima se kompanija može izdvojiti kao protustranka. Cilj je utvrditi utječe li detaljnije oblikovan prompt na ponovljivost ekstrakcije.
Svaka inačica pokreće se deset puta, a rezultat svakog pokretanja promatra se kao skup izdvojenih protustranaka. Prije usporedbe nazivi se normaliziraju uklanjanjem razlika u pisanju velikih i malih slova, interpunkciji i uobičajenim pravnim nastavcima. Spajaju se i poznate inačice naziva iste kompanije. Tako se, primjerice, „AMD” izjednačava s nazivom „Advanced Micro Devices, Inc.”, „Leadtek” s nazivom „Leadtek Research Inc.”, a „Mega Bank” s nazivom „Mega International Commercial Bank”. Izvorni nazivi ostaju sačuvani radi sljedivosti, dok se Jaccardov indeks računa nad njihovim kanonskim nazivima.
Za dva skupa protustranaka A i B Jaccardov indeks definira se izrazom:
J(A,B)=|A∩B|/|A∪B|
Brojnik označuje broj protustranaka zajedničkih objema pokretanjima, a nazivnik ukupan broj različitih protustranaka pronađenih u barem jednom od njih. Vrijednost indeksa može biti između 0 i 1. Vrijednost 1 označuje potpuno jednake skupove, dok vrijednost 0 znači da uspoređena pokretanja nemaju nijednu zajedničku protustranku.
Ako se pojedina inačica prompta pokrene n puta, broj jedinstvenih parova pokretanja izračunava se izrazom:
N_parova=n(n-1)/2
Deset pokretanja čini 45 jedinstvenih parova. Jaccardov indeks izračunava se za svaki par, nakon čega se kao ukupna mjera ponovljivosti navodi aritmetička sredina dobivenih vrijednosti:
J ̅=1/45 ∑_(i=1)^9▒∑_(j=i+1)^10▒J(S_i,S_j )
Pritom Sᵢ označuje skup protustranaka dobiven u i-tom pokretanju, a Sⱼ skup protustranaka dobiven u j-tom pokretanju. Radi lakšeg tumačenja konačna se vrijednost prikazuje i kao postotak. Primjerice, prosječni Jaccardov indeks od 0,80 označuje prosječnu sličnost skupova od 80 %.
Uz prosječni Jaccardov indeks prikazuju se njegova najmanja i najveća vrijednost te učestalost pojavljivanja svake protustranke, primjerice 10/10 ili 6/10. Ti podaci nisu zasebne glavne metrike, nego služe detaljnijem tumačenju razlika među pokretanjima.
Uz ponovljivost se bilježi i postoji li polje Evidence uz svaku izdvojenu protustranku. Ne procjenjuju se njegov sadržaj ni točnost, nego samo je li ga model uključio u rezultat. Postojanje polja označuje se vrijednošću da ili ne.
Mjerenje je provedeno nad godišnjim izvješćem društva Super Micro Computer, Inc. Upotrijebljeno je izvješće Form 10-K za razdoblje završeno 30. lipnja 2025., predano sustavu SEC EDGAR 28. kolovoza 2025. pod pristupnim brojem 0001375365-25-000027.
Za obradu pojedinih dijelova izvješća korišten je model deepseek-v4-flash, dok je za objedinjavanje nalaza i oblikovanje konačnog rezultata korišten model deepseek-v4-pro. Oba je modela pružao DeepSeek. Najveći broj izlaznih tokena iznosio je 16.000 za agente-radnike i vodećeg agenta, dok je ponovljenom pozivu agenta-radnika nakon nepotpunog odgovora bilo dopušteno do 32.000 tokena. Temperatura nije bila izričito postavljena, pa se primjenjivala zadana vrijednost pružatelja uz uključen način razmišljanja.
U mjerenju su korištene dvije inačice prompta za agente-radnike. Blaža inačica sadržavala je osnovne upute za izdvajanje protustranaka, dok je stroža detaljnije određivala uvjete koje poslovni odnos mora ispuniti da bi se kompanija smatrala protustrankom. Prompt vodećeg agenta ostao je jednak u oba slučaja, pa se postupak razlikovao samo u promptu agenata-radnika.
Mjerenje kroz razgovor
Prvi dio prikazuje po jednu uobičajenu korisničku interakciju za svaku inačicu prompta. Izvješće se stoga obrađuje dvaput: jedanput s blažim i jedanput sa strožim promptom.
Iz konačnog odgovora LLM chata bilježe se ukupan broj izdvojenih protustranaka, njihov popis te postoji li uz svaku protustranku evidence.
Jaccardov indeks u ovom se dijelu ne izračunava jer je dobiven samo jedan skup rezultata. Svrha je prikazati rezultat pojedinačne interakcije s LLM chatom, koji se zatim može usporediti s rezultatima ponovljenih ekstrakcija u sljedećem potpoglavlju.
Rezultati pojedinačne ekstrakcije putem LLM chata prikazani su u Tablici 1. Mjerenje je provedeno jedanput s blažom i jedanput sa strožom inačicom prompta nad istim financijskim izvješćem.
Inačica prompta	Broj izdvojenih protustranaka	Protustranke s poljem Evidence
Blaža	15	15
Stroža	9	9
Tablica 1. Usporedba rezultata ekstrakcije putem interaktivnog razgovora
Svih devet protustranaka dobivenih strožom inačicom prompta pojavilo se i u rezultatu blaže inačice. Blaža inačica dodatno je izdvojila društva Broadcom Inc., Samsung Electronics Company Limited, Micron Technology, Inc., Mega International Commercial Bank, Yuanta Commercial Bank Co., Ltd. i U.S. Bank Trust Company, National Association. Polje Evidence bilo je prisutno uz svaku izdvojenu protustranku u obama pokretanjima. U skladu s definiranom metodologijom provjeravano je samo postojanje tog polja, a ne sadržajna točnost priloženoga dokaznog isječka.
Blaža inačica prompta izdvojila je više protustranaka od strože inačice, što je u skladu s njezinim manje restriktivnim uvjetima izdvajanja. Međutim, na temelju samo jednoga pokretanja po inačici prompta nije moguće zaključiti je li razlika posljedica promjene prompta ili varijabilnosti jezičnog modela. Na temelju tih rezultata također nije moguće donijeti zaključak o ponovljivosti ekstrakcije. Zato se u sljedećem potpoglavlju analiziraju rezultati deset ponovljenih ekstrakcija za svaku inačicu prompta.
Mjerenje pomoću measurement consumera
U drugom dijelu komponenta measurement consumer pokreće deset zasebnih ekstrakcija nad istim financijskim izvješćem. Svako pokretanje uključuje novo skeniranje agenata-radnika i novi poziv vodećem agentu. Rezultati svih deset pokretanja ravnopravno se uključuju u izračun.
Za svako se pokretanje bilježe broj protustranaka koje su izdvojili agenti-radnici, broj protustranaka u konačnom rezultatu vodećeg agenta te moguće pogreške. Jaccardov indeks izračunava se nad konačnim skupovima protustranaka vodećeg agenta jer oni predstavljaju izlaz procesa koji se prikazuje korisniku. Rezultati dviju inačica prompta obrađuju se zasebno, nakon čega se uspoređuju njihove konačne vrijednosti.
Za svaku su inačicu prompta uspoređeno svih 45 jedinstvenih parova dobivena iz deset pokretanja. Prije izračuna provedena je definirana normalizacija naziva, koja uključuje spajanje varijanti poput AMD i Advanced Micro Devices, Inc. te Leadtek i Leadtek Research Inc. Jaccardov indeks zatim je izračunan nad normaliziranim skupovima protustranaka iz konačnih rezultata vodećeg agenta.
Inačica prompta	Prosječni Jaccardov indeks	Najmanja sličnost	Parovi s najmanjom sličnošću	Najveća sličnost	Parovi s najvećom sličnošću
Blaža	0,711	0,500	3-6, 4-6, 6-8	1,000	2-10
Stroža	0,671	0,429	5-6	1,000	1-10, 2-3, 2-4, 3-4
Tablica 2. Ponovljivost ekstrakcije izmjerena Jaccardovim indeksom
Blaža inačica prompta ostvarila je prosječni Jaccardov indeks od 0,711, odnosno prosječnu sličnost skupova od 71,14 %. Najmanja zabilježena sličnost iznosila je 0,500 i pojavila se u tri parova pokretanja. Pokretanja 2 i 10 proizvela su potpuno jednake skupove protustranaka.
Stroža inačica prompta ostvarila je nešto niži prosječni Jaccardov indeks od 0,671, odnosno prosječnu sličnost skupova od 67,08 %. Najmanja sličnost zabilježena je između pokretanja 5 i 6 te je iznosila 0,429. Potpuno jednaki skupovi protustranaka dobiveni su u četirima parovima pokretanja.
Učestalost pojavljivanja pojedinih protustranaka u konačnim rezultatima vodećeg agenta prikazana je u Tablici 3. Učestalosti su izračunane nakon normalizacije naziva, zbog čega se varijante poput AMD i Advanced Micro Devices, Inc. promatraju kao ista protustranka.

Normalizirana protustranka	Blaža inačica	Stroža inačica
Ablecom Technology, Inc.	10/10	10/10
BDO USA, P.C.	10/10	10/10
Compuware Technology, Inc.	10/10	10/10
Deloitte & Touche LLP	10/10	10/10
Green Earth Liang's Inc.	10/10	10/10
Leadtek Research Inc.	10/10	10/10
NVIDIA Corporation	10/10	6/10
Intel Corporation	9/10	6/10
AMD	9/10	6/10
U.S. Bank Trust Company, National Association	9/10	6/10
Mega International Commercial Bank	8/10	3/10
Yuanta Commercial Bank Co., Ltd.	5/10	1/10
E.SUN Bank	4/10	2/10
HSBC Bank	4/10	2/10
CTBC Bank Co., Ltd.	3/10	0/10
Broadcom Inc.	2/10	0/10
Micron Technology, Inc.	2/10	0/10
Samsung Electronics Company Limited	2/10	0/10
Corporate Venture	2/10	2/10
Tablica 3. Učestalost pojavljivanja normaliziranih protustranaka u konačnim rezultatima deset pokretanja.
Izraz Corporate Venture nije naziv kompanije, nego pogrešno izdvojena stavka. U tablici je zadržan zato što se pojavio u konačnim rezultatima vodećeg agenta i utjecao na izračun Jaccardova indeksa.
Šest protustranaka pojavilo se u svih deset pokretanja obiju inačica prompta: Ablecom Technology, BDO USA, Compuware Technology, Deloitte & Touche, Green Earth Liang’s i Leadtek Research. Najveća razlika između promptova zabilježena je kod društva Mega International Commercial Bank, koje se pojavilo u osam pokretanja blaže i trima pokretanjima strože inačice. Razlika od četiri pokretanja zabilježena je kod društava NVIDIA i Yuanta Commercial Bank. Broadcom, Micron Technology, Samsung Electronics i CTBC Bank pojavili su se samo u rezultatima blaže inačice. Tablica također pokazuje da stabilno pojavljivanje pojedinih protustranaka ne znači da je čitav rezultat stabilan jer su se ostale stavke razlikovale između pokretanja.
Zaključak mjerenja
Rezultati pokazuju da je blaža inačica prompta u ovom mjerenju ostvarila nešto veću ponovljivost od strože inačice. Razlika između njihovih prosječnih Jaccardovih indeksa iznosi 0,0406, odnosno 4,06 postotnih bodova. Moguće je da dodatni uvjeti strože inačice uvode više graničnih procjena o tome predstavlja li pojedina kompanija stvarnu protustranku, što može povećati varijabilnost rezultata. To je samo moguće objašnjenje dobivene razlike, a ne dokazani uzročni odnos. Zbog malog broja pokretanja i uporabe samo jednoga financijskog izvješća nije ispitano je li zabilježena razlika statistički značajna niti bi li se ponovila na drugim izvješćima. Nijedna inačica prompta nije proizvela potpuno stabilan rezultat u svih deset pokretanja.
U rezultatima obiju inačica prompta vodeći je agent u dvama pokretanjima izdvojio izraz „Corporate Venture” kao protustranku, iako on nije naziv kompanije. Taj primjer pokazuje da visoka ponovljivost ne podrazumijeva nužno i točnost ekstrakcije. U izvornim rezultatima pojavile su se i različite varijante naziva, poput Leadtek i Leadtek Research Inc. te AMD i Advanced Micro Devices, Inc. Njihovom normalizacijom spriječeno je da različiti zapisi naziva iste kompanije neopravdano smanje vrijednost Jaccardova indeksa.
Polje ,,Evidence ’’bilo je prisutno uz svaku izdvojenu protustranku u svih deset pokretanja blaže i svih deset pokretanja strože inačice prompta. Nije zabilježena nijedna stavka bez pripadajućega dokaza, što pokazuje da je proces bio potpuno dosljedan u uključivanju tog polja. Budući da sadržaj isječaka nije međusobno uspoređivan niti je provjeravana njihova točnost, nije moguće zaključiti jesu li dokazi bili sadržajno jednaki, stabilni ili ispravno povezani s izdvojenim protustrankama.
Ograničenja mjerenja
Mjerenje je provedeno nad jednim financijskim izvješćem, uporabom jedne kombinacije dvaju modela istog pružatelja i kroz ukupno 20 pokretanja, odnosno deset za svaku inačicu prompta. Dobiveni se rezultati zato ne mogu izravno primijeniti na druga izvješća, kompanije ili jezične modele.
Budući da nije unaprijed izrađen referentni popis svih stvarnih protustranaka u izvješću, mjerenjem se može procijeniti ponovljivost ekstrakcije, ali se ne mogu pouzdano izračunati njezina preciznost, odziv i ukupna točnost. Jaccardov indeks pritom pokazuje sličnost dobivenih skupova, ali ne otkriva ponavlja li model sustavno isti pogrešan rezultat.
Za polje Evidence provjeravano je samo njegovo postojanje. Sadržaj dokaznih isječaka, njihova međusobna podudarnost i ispravnost povezivanja s izdvojenim protustrankama nisu provjeravani. Stoga potpuna prisutnost tog polja ne potvrđuje utemeljenost ni točnost rezultata.







