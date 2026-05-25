HOW TO WRITE TESTS.

## Integracijski testovi API endpointa

Integracijski testovi ne služe tome da se napravi velika količina umjetnih testova koji samo prolaze. Cilj je dokazati da aplikacija radi kroz stvarni HTTP sloj: ruta, model binding, validacija, autorizacija, baza i JSON odgovor moraju raditi zajedno.

Cilj testova nije samo dokazati da metoda trenutno radi. Glavna vrijednost testova je regresijska zaštita: kada se kasnije promijeni kod, testovi brzo pokažu je li nešto neočekivano puklo.

Za [ASP.NET](http://ASP.NET) Core se najčešće koristi `WebApplicationFactory`, ali sama factory klasa nije najvažniji dio. Najvažnije je kontrolirati testno okruženje: bazu, konfiguraciju, seed podataka i vanjske ovisnosti.

Primjer paketa:

- `Microsoft.AspNetCore.Mvc.Testing`
- `Microsoft.EntityFrameworkCore.InMemory`
- `xunit`
- `FluentAssertions` po želji

### Prvo dokazati jedan end-to-end scenarij

Prije nego se AI-u zada da generira cijeli paket testova, treba **polu-ručno** složiti jedan kvalitetan integracijski test end-to-end.

Cilj nije odmah generirati 50 AI-generiranih testova, nego prvo dokazati obrazac:

1. odabrati jedan vertikalni scenarij, primjerice `GET /api/quiz/{id}` ili `POST /api/quiz`
2. ručno ili uz pažljivo vođenje AI-a složiti `WebApplicationFactory`, testnu konfiguraciju i testnu bazu
3. seedati minimalne podatke potrebne za taj scenarij
4. pozvati stvarni endpoint preko `HttpClient`
5. provjeriti HTTP status, JSON odgovor i stanje baze gdje je bitno
6. tek kada taj test radi i kod izgleda razumljivo, dati AI-u da isti obrazac replicira na ostale CRUD scenarije

Takav pristup smanjuje AI slop: prvi test definira arhitekturu, stil, helper metode i razinu provjere, a AI zatim širi već provjereni obrazac umjesto da izmišlja cijelu testnu strategiju odjednom.

### Testna baza: EF InMemory

Kod aplikacija koje koriste Entity Framework u testovima je najjednostavnije koristiti InMemory provider. Time se izbjegava ovisnost o lokalnom SQL Serveru, connection stringovima i stanju razvojne baze.

Tipična ideja:

- svaki test dobije svoju InMemory bazu
- baza se napuni minimalnim podacima potrebnima za taj test
- test ne ovisi o redoslijedu pokretanja drugih testova
- test ne koristi stvarnu development bazu

Primjer registracije testne baze:

```csharp
builder.ConfigureServices(services =>
{
	var descriptor = services.SingleOrDefault(
		d => d.ServiceType == typeof(DbContextOptions<QuizManagerDbContext>));

	if (descriptor != null)
	{
		services.Remove(descriptor);
	}

	services.AddDbContext<QuizManagerDbContext>(options =>
	{
		options.UseInMemoryDatabase("QuizManagerTests");
	});
});
```

<aside>
⚠️

InMemory baza nije ista kao SQL baza. Ne provjerava sve relacijske constraintove i može se ponašati drugačije kod složenijih queryja. Za ovu vježbu je dobra jer omogućuje fokus na API ponašanje, ali razliku treba razumjeti.

</aside>

### Override konfiguracije u testovima

Aplikacija često čita konfiguraciju iz `appsettings.json`, user secrets, environment varijabli ili connection stringova. Testovi ne smiju ovisiti o stvarnoj konfiguraciji.

U `WebApplicationFactory` se konfiguracija može nadjačati posebno za testove:

```csharp
builder.ConfigureAppConfiguration((context, config) =>
{
	var testSettings = new Dictionary<string, string>
	{
		["ConnectionStrings:DefaultConnection"] = "TestConnection",
		["Authentication:Google:ClientId"] = "test-client-id",
		["Authentication:Google:ClientSecret"] = "test-client-secret"
	};

	config.AddInMemoryCollection(testSettings);
});
```

Ovo je korisno kada aplikacija očekuje da neka vrijednost postoji, ali u testu ne želimo koristiti stvarnu vrijednost.

### Vanjske integracije treba mockati

Ako aplikacija tijekom API poziva šalje email, poziva payment provider, Google, Facebook, storage servis ili bilo koju drugu 3rd party integraciju, test ne bi trebao stvarno zvati taj servis.

Rješenje je takvu funkcionalnost sakriti iza interfacea:

```csharp
public interface IEmailSender
{
	Task SendAsync(string to, string subject, string body);
}
```

U aplikaciji se registrira stvarna implementacija, a u testu mock ili fake implementacija:

```csharp
builder.ConfigureServices(services =>
{
	services.AddSingleton<IEmailSender, FakeEmailSender>();
});
```

<aside>
ℹ️

Mockati treba granice sustava: vanjske servise, email, file storage, payment gatewaye i slične ovisnosti. Ne treba mockati sve interne klase samo zato što se može.

</aside>

Interface ima smisla kada postoji više implementacija ili kada u testu treba zamijeniti stvarnu vanjsku integraciju fake/mock implementacijom. Ako klasa ima samo jednu implementaciju i ne postoji potreba za zamjenom, interface često samo dodaje nepotrebnu složenost.

### Ne testirati mockove umjesto aplikacije

Česta greška kod AI-generiranih testova je da se napravi previše mockova. Tada test zapravo provjerava da mock vraća ono što smo mu ručno rekli da vrati. Loš obrazac:

❌ mockati repozitorij
❌ mockati service
❌ mockati mapper
❌ mockati validaciju
❌ zatim provjeriti da controller vraća rezultat iz mocka

Takav test ima malu vrijednost jer ne provjerava stvarno ponašanje aplikacije. Bolji obrazac:

✅ pokrenuti aplikaciju kroz `WebApplicationFactory`
✅ koristiti pravi `DbContext` s InMemory bazom
✅ napuniti podatke pomoćnim metodama
✅ pozvati API preko `HttpClient`
✅ provjeriti HTTP status i podatke u odgovoru

Primjer pomoćne metode:

```csharp
private async Task<Quiz> CreateQuizAsync(QuizManagerDbContext dbContext)
{
	var quiz = new Quiz
	{
		Title = "Test",
		Description = "Test quiz"
	};

	dbContext.Quizzes.Add(quiz);
	await dbContext.SaveChangesAsync();

	return quiz;
}
```

Test tada jasno pokazuje što priprema, što poziva i što očekuje.

### Što minimalno testirati

Za svaki API controller treba pokriti osnovne scenarije:

- [ ]  `GET all` vraća uspješan status i kolekciju
- [ ]  `GET by id` vraća zapis ako postoji
- [ ]  `GET by id` vraća `404` ako zapis ne postoji
- [ ]  `POST` kreira zapis i vraća `201 Created`
- [ ]  `POST` vraća grešku za validacijski neispravan model
- [ ]  `PUT` mijenja postojeći zapis
- [ ]  `PUT` vraća grešku za nepostojeći zapis
- [ ]  `DELETE` briše postojeći zapis
- [ ]  `DELETE` vraća grešku za nepostojeći zapis
- [ ]  zaštićeni endpointi vraćaju odgovarajući status ako korisnik nije autoriziran

### Dobar integracijski test

Dobar integracijski test ima jasnu strukturu:

1. **Arrange** — pripremi testnu bazu, konfiguraciju i potrebne podatke
2. **Act** — pozovi stvarni API endpoint preko `HttpClient`
3. **Assert** — provjeri status kod, JSON odgovor i stanje baze ako je potrebno

Primjer:

```csharp
[Fact]
public async Task GetById_ShouldReturnQuiz_WhenQuizExists()
{
	using var scope = this._factory.Services.CreateScope();
	var dbContext = scope.ServiceProvider.GetRequiredService<QuizManagerDbContext>();
	var quiz = await CreateQuizAsync(dbContext);

	var response = await this._client.GetAsync($"/api/quiz/{quiz.ID}");

	response.StatusCode.Should().Be(HttpStatusCode.OK);

	var dto = await response.Content.ReadFromJsonAsync<QuizDTO>();
	dto.ID.Should().Be(quiz.ID);
	dto.Title.Should().Be(quiz.Title);
}
```
