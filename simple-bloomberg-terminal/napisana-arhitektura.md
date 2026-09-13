5.	Arhitektura sustava
      Aplikacija omogućuje dohvaćanje podataka o kompanijama putem vanjskih API-ja, pretraživanje mrežnih izvora i izdvajanje podataka iz dokumenata pomoću velikih jezičnih modela. Zbog opsega rada, u ovom je poglavlju prikazana samo arhitektura izdvajanja podataka iz javno dostupnih SEC izvješća.
      Postupak započinje dohvaćanjem izvornog dokumenta iz sustava SEC EDGAR. Nakon parsiranja, na temelju unaprijed poznatih naziva poglavlja [20] izdvajaju se dijelovi važni za analizu troškova, prihoda ili rizika. Tekst se zatim dijeli na manje cjeline koje paralelno obrađuju brzi jezični modeli. Njihovi se nalazi objedinjuju i predaju vodećem modelu, koji uklanja ponavljanja i oblikuje konačan rezultat.
      Rezultate ekstrakcije koriste dva consumera: LLM Chat za razgovor s korisnikom i pregled nalaza te measurement za mjerenje kvalitete i stabilnosti rezultata. Cijeli je postupak prikazan na Slici 1 i može se sažeti u pet koraka:
1.	Dohvaća se izvorni dokument iz sustava SEC EDGAR.
2.	Izdvajaju se dijelovi dokumenta važni za troškove, prihode ili rizike.
3.	Odabrani tekst dijeli se na manje cjeline.
4.	Brzi jezični modeli paralelno obrađuju cjeline i izdvajaju moguće podatke s pripadajućim dokazima.
5.	Vodeći model objedinjuje nalaze i oblikuje konačan rezultat.

Slika 1. Arhitektura ekstrakcije
5.1.	 Dohvat izvornog dokumenta
Proces ekstrakcije započinje dohvatom izvornog financijskog izvješća iz arhive SEC EDGAR. Za to je zadužen poseban servis koji od ostatka aplikacije odvaja izradu adrese dokumenta i HTTP komunikaciju.
Metoda ,, GetFilingDocument’’ prima CIK oznaku kompanije, broj prijave bez crtica i naziv primarnog dokumenta. Na temelju tih podataka sastavlja adresu, dohvaća dokument i vraća njegov sadržaj kao tekst. Ako traženi dokument ne postoji, metoda vraća vrijednost null.
public async Task<string?> GetFilingDocument(
string cik, string accessionNoDashes, string primaryDocument)
{
var url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{accessionNoDashes}/{primaryDocument}";
var resp = await _http.GetAsync(url);

    if (resp.StatusCode == HttpStatusCode.NotFound)
        return null;

    resp.EnsureSuccessStatusCode();

    return await resp.Content.ReadAsStringAsync();
}
Isječak programskog koda 1. Dohvat primarnog dokumenta iz arhive SEC EDGAR
5.2.	 Odabir važnih dijelova dokumenta
SEC izvješća mogu biti vrlo duga, a svi njihovi dijelovi nisu jednako važni za svaku vrstu ekstrakcije. Obrada cijelog dokumenta povećala bi vrijeme i trošak izvođenja te bi modelu predala velik dio nepotrebnog teksta. Zato se za svaku vrstu ekstrakcije unaprijed određuju relevantne SEC stavke. SEC stavke unaprijed su određene brojevima i naslovima što je navedeno na početku poglavlja.
Za izdvajanje rizika koriste se stavke 1A i 7A, za troškove stavke 1, 7 i 8, a za prihode stavke 1, 1A, 7 i 8. Najveća duljina pojedine tekstne cjeline ograničena je na 4.000 znakova, dok jedan dokument može sadržavati najviše 48 cjelina za obradu. Time se sprječava stvaranje prevelikog broja LLM poziva kod neuobičajeno opsežnih dokumenata.
public static string[] ItemsFor(ExtractionNode node) => node switch
{
ExtractionNode.RISK => ["1A", "7A"],
ExtractionNode.COST => ["1", "7", "8"],
_ => ["1", "1A", "7", "8"],
};

public const int MaxChunkChars = 4000;
public const int MaxScanChunks = 48;
Isječak programskog koda 2. Odabir SEC stavki prema vrsti ekstrakcije
Nakon odabira relevantnih stavki njihov se sadržaj pretvara u tekst i dijeli na manje cjeline. Svaka se cjelina sprema zajedno s oznakom stavke iz koje potječe, što omogućuje praćenje njezina položaja u izvornom dokumentu. Broj cjelina ograničen je po pojedinoj stavci i na razini cijelog dokumenta.
public static List<FilingChunk> Build(string raw, string[] items)
{  
var text = ToText(raw);
var chunks = new List<FilingChunk>();
foreach (var item in items)
{
var body = SectionBody(text, item);
if (body is null) continue;
int n = 0;
foreach (var chunk in Paragraphs(body))
{
chunks.Add(
new FilingChunk($"Item {item}", chunk, $"Item {item}"));
if (++n >= MaxChunksPerSection) break;
if (chunks.Count >= MaxScanChunks) return chunks;
}
}
return chunks;
}
Isječak programskog koda 3. Pretvaranje odabranih stavki u manje tekstne cjeline
5.3.	 Paralelna ekstrakcija brzim LLM radnicima
Obrada cijelog dokumenta jednim LLM pozivom bila bi spora i otežala bi prepoznavanje svih važnih podataka kao što je opisano u poglavlju 3.3. Zato brzi LLM radnici zasebno obrađuju manje tekstne cjeline. Svaki radnik ima jednostavan zadatak: pronaći moguće podatke i vratiti ih zajedno s dokazom iz izvornog teksta. Budući da cjeline ne ovise jedna o drugoj, mogu se obrađivati paralelno.
Servis za svaku cjelinu pokreće brži model i zahtijeva odgovor u JSON formatu. Istodobno se može izvoditi najviše šest radnika, čime se ubrzava obrada bez pokretanja neograničenog broja poziva. Nakon završetka obrade rezultati svih radnika spajaju se, a kandidati jednakog naziva objedinjuju u jedan nalaz.
private async Task<List<ExtractionSuggestion>> RunFastWorkerAgentsAsync(
IReadOnlyList<FilingChunk> chunks, ExtractionNode node, Action<FastWorkerScanProgress>? onProgress,
bool strictCounterparties, List<ExtractionSuggestion>? workerClaims, CancellationToken ct)
{
using var gate = new SemaphoreSlim(MaxParallelFastWorkers);
var perChunk = await Task.WhenAll(chunks.Select((c, i) =>
RunFastWorkerAgentAsync(c, i, node, gate, onProgress, strictCounterparties, ct)));
workerClaims?.AddRange(perChunk.SelectMany(claims => claims));
var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
foreach (var list in perChunk)
foreach (var s in list)
{
if (string.IsNullOrWhiteSpace(s.Name)) continue;
byName[s.Name] = byName.TryGetValue(s.Name, out var seen) ? MergeSuggestions(seen, s) : s;
}
return byName.Values.ToList();
}
Isječak programskog koda 4. Paralelno pokretanje brzih LLM radnika
Za obradu pojedine cjeline izrađuju se sistemski prompt i korisnička poruka koja sadrži oznaku SEC stavke i pripadajući tekst. Postavka Fast: true usmjerava zahtjev prema brzom modelu, dok JsonObject: true određuje da odgovor mora biti vraćen u strukturiranom JSON obliku. Dobiveni se odgovor zatim pretvara u popis prijedloga za ekstrakciju.
var system = FastWorkerPromptFor(node, strictCounterparties);

var prompt =
$"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";

var completion = await _llm.CompleteAsync(
new ChatRequest(
system,
prompt,
FastWorkerMaxTokens,
JsonObject: true,
Fast: true),
ct);

var found = ParseFastWorkerResponse(
completion.Content, chunk.Section, node).ToList();
Isječak programskog koda 5. LLM obrada pojedine tekstne cjeline
5.4.	 Zajednički kontekst za vodeći model
Rezultati brzih LLM radnika moraju se pripremiti u obliku koji vodeći model može koristiti neovisno o tome je li proces pokrenuo LLM chat ili measurement consumer. Za to je zadužen zajednički servis koji povezuje brze radnike s vodećim modelom. Time se izbjegava da svaki consumer zasebno dohvaća dokument, pokreće skeniranje i priprema kontekst.
Servis koristi sažetak nalaza koji mu je izravno predan ili sam pokreće novo brzo skeniranje. Rezultati LLM radnika ne dohvaćaju se iz predmemorije, pa kontekst uvijek potječe iz trenutačnog izvođenja. Ako brzi radnici ne pronađu nijedan nalaz, servis vraća prazan kontekst. Vodeći model tako dobiva isključivo rezultate trenutačnog skeniranja.
public async Task<string> BuildAsync(
long companyId,
string accession,
string doc,
ExtractionNode node,
bool scanIfMissing = true,
string? fastWorkerDigest = null,
CancellationToken ct = default)
{
if (string.IsNullOrWhiteSpace(accession) ||
string.IsNullOrWhiteSpace(doc))
{
return "";
}

    var digest = fastWorkerDigest is not null
        ? fastWorkerDigest
        : scanIfMissing
            ? await _fastWorkerScan.CreateFastWorkerDigestAsync(
                companyId,
                accession,
                doc,
                node,
                ct)
            : "";

    return string.IsNullOrEmpty(digest)
        ? ""
        : "\n\n" + digest;
}
Isječak programskog koda 6. Izgradnja zajedničkog konteksta dokumenta
5.5.	 Vodeći LLM kao završni korak ekstrakcije
Nalazi brzih radnika još nisu konačan rezultat. Vodeći LLM povezuje ih sa sistemskim promptom i oblikuje završni odgovor. On je posljednji dio zajedničkog procesa ekstrakcije, dok LLM Chat i Measurement consumer određuju na koji će način njegov odgovor biti preuzet.
Za potrebe mjerenja koristi se potpuni odgovor. Aplikacija čeka završetak generiranja kako bi rezultat mogla parsirati i analizirati. U slučaju privremene komunikacijske pogreške poziv se može jednom ponoviti. Za razgovor se koristi strujani prijenos, pri kojem se odgovor korisniku prikazuje u dijelovima čim ga model počne stvarati.
public async Task<LlmCompletion> CompleteAsync(
string systemPrompt, string filingContext, string userPrompt, int maxTokens,
CancellationToken ct = default)
{
var request = new ChatRequest(
systemPrompt + filingContext,
userPrompt,
MaxTokens: maxTokens);

    for (var attempt = 1; ; attempt++)
    {
        try
        {
            return await llm.CompleteAsync(request, ct);
        }
        catch (Exception ex) when (
            attempt < MaxCompletionAttempts &&
            !ct.IsCancellationRequested &&
            ex is HttpRequestException or IOException or TaskCanceledException)
        {
            logger.LogWarning(
                ex,
                "Lead-agent completion transport failed on attempt {Attempt}/{MaxAttempts}; retrying",
                attempt,
                MaxCompletionAttempts);
        }
    }
}

public async IAsyncEnumerable<ChatDelta> StreamAsync(
string systemPrompt, string filingContext, IReadOnlyList<LlmMessage> messages,
[EnumeratorCancellation] CancellationToken ct = default)
{
var request = new List<LlmMessage>
{
new("system", systemPrompt + filingContext)
};
request.AddRange(messages);

    await foreach (var delta in llm.StreamAsync(request, ct: ct))
        yield return delta;
}
Isječak programskog koda 7. Dva načina vraćanja odgovora vodećeg LLM-a.
Obje metode koriste isti sistemski prompt i zajednički kontekst dobiven prethodnim koracima. Razlikuju se samo u načinu vraćanja odgovora: ,,CompleteAsync’’ vraća cijeli rezultat odjednom, dok ga ,,StreamAsync’’ vraća postupno.
5.6.	 Prvi consumer: razgovor s korisnikom
LLM Chat consumer koristi rezultate zajedničkog procesa ekstrakcije kako bi korisniku omogućio pregled pronađenih dobavljača, kupaca ili rizika. Korisnik tako ne mora samostalno pregledavati cijelo financijsko izvješće, nego može postavljati dodatna pitanja i odlučiti koje podatke želi spremiti.
Servis od zajedničkog kontekstnog servisa najprije traži rezultate ekstrakcije. Ako oni još ne postoje, pokreće se brzo skeniranje dokumenta. Povijest razgovora zatim se pretvara u poruke namijenjene jezičnom modelu i zajedno s kontekstom šalje vodećem LLM-u. Za pozivanje vodećeg modela koristi se metoda ,, StreamAsync’’, zbog čega se odgovor korisniku prikazuje postupno dok nastaje.
public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
long companyId, string accession, string doc, ExtractionNode node,
IReadOnlyList<ChatMessage> history,
string? fastWorkerDigest = null,
[EnumeratorCancellation] CancellationToken ct = default)
{
var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
if (hasFiling && fastWorkerDigest is null)
yield return new ChatDelta("status", "Scanning the filing with parallel fast worker agents...");

    var filingContext = await _context.BuildAsync(
        companyId, accession, doc, node,
        scanIfMissing: fastWorkerDigest is null,
        fastWorkerDigest: fastWorkerDigest,
        ct: ct);
    var messages = history
        .Select(message => new LlmMessage(
            message.Role == "assistant" ? "assistant" : "user", message.Content))
        .ToList();
    await foreach (var delta in _leadAgent.StreamAsync(
        LeadAgentPromptFor(node), filingContext, messages, ct))
        yield return delta;
}
Isječak programskog koda 8. Korištenje zajedničkog procesa ekstrakcije u razgovoru.
Ovaj consumer nije dio jezgre ekstrakcije, nego njezin vanjski korisnik. On pokreće zajednički proces, preuzima pripremljeni kontekst i određuje da se odgovor vodećeg modela vrati postupno.
5.7.	 Drugi consumer: mjerenje ekstrakcije
Jedno uspješno izvođenje nije dovoljno za procjenu kvalitete LLM ekstrakcije jer model pri ponavljanju istog postupka može vratiti različite rezultate. Measurement consumer zato više puta pokreće zajednički proces te bilježi pronađene nalaze, njihove dokaze i nastale pogreške. Na temelju toga može se procijeniti kvaliteta i stabilnost rezultata.
Mjerenje je trenutačno usmjereno na izdvajanje troškovnih protustranaka, odnosno dobavljača. Prvo izvođenje služi i za pripremu predmemorije dokumenta i njegovih parsiranih dijelova. Preostala izvođenja mogu se pokretati paralelno, ali svako od njih provodi vlastito brzo skeniranje i zaseban poziv vodećem modelu. Nakon završetka svih izvođenja njihovi se rezultati uspoređuju i iz njih se izračunavaju završne mjere.
var first = await ExecuteRunAsync(
target, node, strictCounterparties, 1, null,
sectionCandidates, keys, model, onProgress, ct);

using var gate = new SemaphoreSlim(MaxParallelRuns);

var rest = await Task.WhenAll(
Enumerable.Range(2, Math.Max(0, runs - 1))
.Select(run => ExecuteRunAsync(
target, node, strictCounterparties, run, gate,
sectionCandidates, keys, model, onProgress, ct)));

return MeasurementCalculator.Calculate(
rest.Prepend(first).ToArray(),
model,
runAt,
sectionCandidates);
Isječak programskog koda 9. Ponavljanje zajedničkog procesa radi mjerenja
U svakom se izvođenju najprije pokreću brzi LLM radnici. Njihov se sažetak pretvara u zajednički kontekst, koji se potom predaje vodećem modelu. Za razliku od LLM Chat consumera, ovdje se koristi metoda ,, CompleteAsync’’ jer mjerenje mora pričekati cijeli odgovor kako bi ga moglo parsirati i usporediti s rezultatima ostalih izvođenja.
var scanned = await fastWorkerScan.RunFastWorkerScanAsync(
target.CompanyId,
target.Accession,
target.Document,
node,
strictCounterparties: strictCounterparties,
captureArtifacts: true,
ct: ct);

var filingContext = await context.BuildAsync(
target.CompanyId,
target.Accession,
target.Document,
node,
scanIfMissing: false,
fastWorkerDigest: scanned.FastWorkerDigest,
ct: ct);

var completion = await leadAgent.CompleteAsync(
MeasurementPrompts.LeadAgentSystemPrompt,
filingContext,
MeasurementPrompts.LeadAgentUserPrompt,
LeadAgentMaxTokens,
ct);
Isječak programskog koda 10. Jedno izvođenje brzih radnika i vodećeg modela
Measurement consumer nalazi se izvan jezgre ekstrakcije i djeluje kao mjerni omotač oko zajedničkog procesa. Samostalno upravlja brojem izvođenja i njihovim paralelnim pokretanjem, ali u svakom izvođenju koristi iste zajedničke servise za brzo skeniranje, pripremu konteksta i pozivanje vodećeg modela.
5.8.	 Predmemorija i ponovno korištenje podataka
Isti SEC dokument može se više puta obrađivati tijekom razgovora s korisnikom ili provođenja mjerenja. Ponovno preuzimanje dokumenta stvaralo bi nepotrebne zahtjeve i povećalo operacije izvođenja. Zbog toga se izvorni dokument i rezultati njegova parsiranja privremeno spremaju u predmemoriju.
U predmemoriji se čuvaju izvorni sadržaj SEC dokumenta i podnaslovi pronađeni unutar relevantnih stavki. Riječ je o determinističkim podacima koji se za isti dokument ne mijenjaju između izvođenja. Rezultati brzih LLM radnika ne spremaju se jer se pri svakom izvođenju trebaju ponovno generirati. Vrijeme čuvanja podataka u predmemoriji iznosi 30 minuta.
Ključevi predmemorije sadrže pristupni broj prijave i naziv dokumenta. Ključ za pronađene podnaslove dodatno sadrži vrstu ekstrakcije jer se relevantni dijelovi dokumenta razlikuju ovisno o tome izdvajaju li se rizici, troškovi ili prihodi.
private static string HeadingsKey(
string accession,
string document,
ExtractionNode node) =>
$"filing-headings:{node}:{accession}:{document}";

public static string RawKey(
string accession,
string document) =>
$"filing-raw:{accession}:{document}";
private static readonly TimeSpan CacheFor =
TimeSpan.FromMinutes(30);
Isječak programskog koda 11. Ključevi i trajanje predmemorije podataka dokumenta.
Pri svakoj novoj poruci ponovno se pokreću brzi LLM radnici i vodeći model. Brzi radnici mogu koristiti dokument i podnaslove iz predmemorije, ali svaki put stvaraju nove rezultate koje izravno prosljeđuju vodećem modelu. Povijest razgovora vodi se odvojeno i također mu se šalje pri svakom zahtjevu. Predmemorija tako sadrži samo podatke dobivene determinističkom obradom dokumenta.
Isti se postupak primjenjuje tijekom mjerenja. Prvi se prolaz izvodi zasebno kako bi se dokument dohvatio, parsirao i spremio u predmemoriju, nakon čega se ostali prolazi mogu paralelno pokrenuti koristeći iste podatke dokumenta.
5.9.	 Izlazni podatkovni model
Nakon ekstrakcije rezultat jezičnog modela pretvara se u unaprijed definirane modele kako bi ga aplikacija mogla prikazati, usporediti ili spremiti.
U razgovoru vodeći model uz tekstualni odgovor vraća save blok u JSON formatu. Aplikacija ga pretvara u ,,SaveBatchItem’’, koji sadrži podatke o pronađenoj protustranci i dokaz iz izvornog dokumenta. Zapisi se spremaju tek nakon korisničke potvrde.
public class SaveBatchItem
{
public string Name { get; set; } = string.Empty;
public string? Classification { get; set; }
public string? Note { get; set; }
public string? RelatedCompany { get; set; }
public string? RelatedCompanyTicker { get; set; }
public string? Reference { get; set; }
public string? Evidence { get; set; }
}
Isječak programskog koda 12. Model zapisa za spremanje kod LLM chat-a.
Klijentski kod pronalazi save blokove i njihov JSON sadržaj pretvara u objekte. Neispravni zapisi preskaču se, a zapisi s jednakim nazivom objedinjuju.
function parseSaves(id) {
const byName = new Map();
const re = /```save\s*([\s\S]*?)```/g;

    for (const m of read(chatKey(id), [])) {
        if (m.role !== 'assistant') continue;

        let x;
        re.lastIndex = 0;

        while ((x = re.exec(m.content)) !== null) {
            let j;

            try {
                j = JSON.parse(x[1].trim());
            } catch {
                continue;
            }

            const s = normalizeSave(j);

            if (s.name) {
                s.key = s.name;
                byName.set(s.name, s);
            }
        }
    }

    return [...byName.values()];
}
Isječak programskog koda 14. Čitanje JSON zapisa iz odgovora modela
,,SaveBatchItem” je prijenosni model i ne sprema se izravno u bazu podataka. Ovisno o vrsti ekstrakcije, njegov se sadržaj nakon potvrde preslikava u ,,RevenueSource”, ,,CostSource” ili ,,CompanyRisk”.
SaveBatchItem	RevenueSource / CostSource	CompanyRisk
Name	Name	Name
Classification	—	Scope
Note	—	Note
RelatedCompany	služi za pronalazak ili stvaranje povezane kompanije	—
RelatedCompanyTicker	služi za identifikaciju povezane kompanije	—
Reference	Reference	Reference
Evidence	Evidence	Evidence
odabrana kompanija	CompanyId	CompanyId
odabrana SEC prijava	FilingId	FilingId
Tablica 1. Preslikavanje SaveBatchItem-a u entitetski model
,,Company” je središnji entitet prikazanog modela. Svaki rezultat ekstrakcije povezan je s analiziranom kompanijom preko ključa ,,CompanyId” te s izvornom SEC prijavom preko ključa ,,FilingId”. Polja ,,Reference” i ,,Evidence” pritom čuvaju lokaciju i doslovni dokaz iz dokumenta.
Kod izvora prihoda i troškova pronađena protustranka također može biti predstavljena entitetom, ,Company”. Aplikacija je pronalazi ili, ako ne postoji, stvara novi zapis, a vezu pohranjuje u polje RelatedCompanyId. Entitet CompanyRisk nema povezanu protustranku, nego sadrži područje i opis rizika.

Slika 2. Pojednostavljeni entitetski model rezultata ekstrakcije.
Mjerenje koristi model ,,CounterpartyMeasurementResult’’, koji objedinjuje rezultate svih izvođenja nad istim dokumentom. Na temelju njegovih redaka uspoređuju se rezultati brzih radnika i vodećeg modela te izračunavaju pokazatelji mjerenja.
public sealed record CounterpartyMeasurementResult(
string Company,
string Cik,
string Accession,
int Runs,
int TotalErrors,
string Model,
DateTime RunAt,
IReadOnlyList<CounterpartyMeasurementRow> Rows,
string? Error = null);
Isječak programskog koda 13. Model objedinjenog rezultata mjerenja
5.10.	Prompt specifikacija
