### Nužni uvjeti za predaju vježbe

- [ ]  **Implementirati kompletnu API podršku za sve entitete (CRUD, DTO) (2 boda)**
    - [ ]  API controlleri moraju podržavati osnovne CRUD operacije za sve entitete gdje poslovna pravila to dopuštaju
        - [ ]  `GET` za dohvat svih zapisa, uz opciju pretrage (query i jos ponesto)
        - [ ]  `GET` za dohvat jednog zapisa po ID-u
        - [ ]  `POST` za kreiranje zapisa
        - [ ]  `PUT` za izmjenu zapisa
        - [ ]  `DELETE` za brisanje zapisa
    - [ ]  API ne smije direktno izlagati nepotrebna interna polja entiteta
        - [ ]  Koristiti DTO klase za podatke koji se vraćaju API klijentu
        - [ ]  Povezane podatke prikazati kroz ugniježđene DTO klase gdje ima smisla
- [ ]  Upload datoteka mora biti vezan uz konkretnog kviza
    - [ ]  Upload raditi asinkrono preko Dropzone komponente ili održavane alternative
    - [ ]  Datoteke spremiti na disk
    - [ ]  U bazu spremiti metapodatke i putanju
    - [ ]  Popis datoteka učitati AJAX pozivom
    - [ ]  Omogućiti brisanje postojećih datoteka
- [ ]  Autentikacija mora biti uključena kroz [ASP.NET](http://ASP.NET) Core Identity
    - [ ]  Lokalna registracija i prijava moraju raditi
    - [ ]  `AppUser` mora biti proširen traženim poljima
    - [ ]  Autorizacija mora ograničavati akcije prema pravilima zadatka
        - [ ]  Javne akcije dostupne su anonimnim korisnicima
        - [ ]  Create/Edit/Delete dostupni su samo dopuštenim korisnicima
        - [ ]  Role `Admin` i barem još jedna rola moraju biti implementirane
- [ ]  Google ili Facebook login mora raditi
- [ ]  Integracijski testovi moraju pokriti API endpointe za sve CRUD operacije
    - [ ]  Testirati uspješne scenarije
    - [ ]  Testirati nepostojeće ID-eve
    - [ ]  Testirati validacijske pogreške gdje postoje


## Dropzone

Upload datoteka čest je zahtjev u web aplikacijama. Korisnik treba moći odabrati jednu ili više datoteka, a aplikacija ih treba spremiti na server i povezati s odgovarajućim entitetom. Dropzone je JavaScript komponenta koja omogućuje intuitivan upload datoteka. Datoteke šalje asinkrono na URL definiran u formi. Server je odgovoran za:

- prihvat datoteke
- validaciju datoteke
- spremanje datoteke na disk ili storage
- spremanje metapodataka u bazu
- povezivanje datoteke s entitetom

Originalni Dropzone projekt nije aktivno održavan kao ranije, ali i dalje može raditi za potrebe vježbe. Moguće je koristiti i održavani fork ili sličnu biblioteku.

<aside>
ℹ️

Za ovu vježbu bitan je koncept asinkronog uploada i povezivanja datoteke s kvizom. Sama biblioteka može biti Dropzone ili kompatibilna alternativa.

</aside>

### Gdje spremati datoteke

Za ovu vježbu dovoljno je spremiti datoteke na lokalni disk aplikacije. U stvarnim aplikacijama treba razmisliti o drugačijem storageu:

- relacijska baza najčešće nije dobro mjesto za spremanje većih datoteka
- dokumentna baza može biti prihvatljiva za vrlo male dokumente
- za ozbiljnije aplikacije bolje je koristiti Azure Blob Storage, Amazon S3, Firebase Storage ili sličan servis
- lokalni disk postaje problem kod horizontalnog skaliranja jer više instanci aplikacije nemaju nužno iste datoteke

Ako aplikacija radi u više instanci, jedna instanca može spremiti datoteku lokalno, a drugi zahtjev može završiti na drugoj instanci koja tu datoteku nema. Zato je za produkcijske sustave bolje koristiti zajednički storage servis nego rješavati sinkronizaciju datoteka između instanci.

### Model Attachment

Jedan kviz može imati više datoteka. Zato se uvodi nova klasa `Attachment`.

Primjer:

`Attachment.cs`

```csharp
public class Attachment
{
	public int ID { get; set; }

	public int QuizID { get; set; }
	public Quiz Quiz { get; set; }

	public string FileName { get; set; }
	public string FilePath { get; set; }
	public string ContentType { get; set; }
	public long FileSize { get; set; }

	public DateTime CreatedAt { get; set; }
}
```

U `Quiz` klasu dodati kolekciju:

```csharp
public List<Attachment> Attachments { get; set; }
```

Nakon izmjene modela potrebno je napraviti migraciju i osvježiti bazu.

```bash
dotnet ef migrations add AddQuizAttachments
dotnet ef database update
```

### Dropzone na Edit formi

Upload datoteka treba biti dostupan samo na Edit formi, jer kod Create forme kviz još nema ID. Bez ID-a nije jasno uz koji zapis treba vezati datoteku.

Primjer forme:

```html
<div class="row">
	<div class="col-md-4">
		<form asp-action="Edit">
			<input type="hidden" asp-for="ID" />
			<partial name="_CreateOrUpdate" />
		</form>
	</div>

	<div class="col-md-6">
		<label class="control-label">Dokumenti</label>
		<form id="attachmentDz"
			  asp-controller="Quiz"
			  asp-action="UploadAttachment"
			  asp-route-quizId="@Model.ID"
			  enctype="multipart/form-data"
			  class="dropzone">
		</form>

		<div id="attachmentList"></div>
	</div>
</div>
```

### Uključivanje skripti

Skripte treba uključiti u `Scripts` sekciju pravog viewa. Partial view ne može definirati `@section Scripts`.

```html
@section Scripts {
	<link rel="stylesheet" href="~/lib/dropzone/dropzone.css" />
	<script src="~/lib/dropzone/dropzone.js"></script>

	<script type="text/javascript">
		Dropzone.options.attachmentDz = {
			success: function (file, response) {
				loadAttachments();
			}
		};

		$(function () {
			loadAttachments();
		});

		function loadAttachments() {
			$("#attachmentList").load("@Url.Action("GetAttachments", "Quiz", new { quizId = Model.ID })");
		}
	</script>
}
```

### Upload akcija

Akcija prima ID kviza i datoteku. Dropzone šalje datoteke kao multipart form data.

```csharp
[HttpPost]
public IActionResult UploadAttachment(int quizId, IFormFile file)
{
	var quiz = this._dbContext.Quizzes.FirstOrDefault(c => c.ID == quizId);

	if (quiz == null)
	{
		return NotFound();
	}

	if (file == null || file.Length == 0)
	{
		return BadRequest();
	}

	var uploadsPath = Path.Combine(
		Directory.GetCurrentDirectory(),
		"wwwroot",
		"uploads",
		"quizzes",
		quizId.ToString());

	Directory.CreateDirectory(uploadsPath);

	var fileName = Guid.NewGuid().ToString() + Path.GetExtension(file.FileName);
	var filePath = Path.Combine(uploadsPath, fileName);

	using (var stream = new FileStream(filePath, FileMode.Create))
	{
		file.CopyTo(stream);
	}

	var attachment = new Attachment
	{
		QuizID = quizId,
		FileName = file.FileName,
		FilePath = "/uploads/quizzes/" + quizId + "/" + fileName,
		ContentType = file.ContentType,
		FileSize = file.Length,
		CreatedAt = DateTime.UtcNow
	};

	this._dbContext.Attachments.Add(attachment);
	this._dbContext.SaveChanges();

	return Json(new { success = true });
}
```

<aside>
⚠️

U stvarnoj aplikaciji treba dodatno validirati ekstenziju, MIME tip, veličinu datoteke i naziv datoteke. Nikad se ne smije nekontrolirano vjerovati uploadanom sadržaju.

</aside>

### Popis datoteka

Popis uploadanih datoteka treba dohvatiti AJAX pozivom nakon učitavanja stranice i nakon svakog uspješnog uploada.

Controller akcija:

```csharp
public IActionResult GetAttachments(int quizId)
{
	var attachments = this._dbContext.Attachments
		.Where(a => a.QuizID == quizId)
		.OrderByDescending(a => a.CreatedAt)
		.ToList();

	return PartialView("_AttachmentList", attachments);
}
```

Partial view:

`_AttachmentList.cshtml`

```html
<table class="table table-sm">
	<thead>
		<tr>
			<th>Datoteka</th>
			<th>Veličina</th>
			<th>Akcija</th>
		</tr>
	</thead>
	<tbody>
		@foreach (var attachment in Model)
		{
			<tr>
				<td>
					<a href="@attachment.FilePath" target="_blank">@attachment.FileName</a>
				</td>
				<td>@attachment.FileSize</td>
				<td>
					<button type="button"
							class="btn btn-danger btn-sm"
							onclick="deleteAttachment(@attachment.ID)">
						Obriši
					</button>
				</td>
			</tr>
		}
	</tbody>
</table>
```

### Brisanje datoteka

Brisanje se može napraviti AJAX pozivom.

Controller akcija:

```csharp
[HttpPost]
public IActionResult DeleteAttachment(int id)
{
	var attachment = this._dbContext.Attachments.FirstOrDefault(a => a.ID == id);

	if (attachment == null)
	{
		return NotFound();
	}

	var physicalPath = Path.Combine(
		Directory.GetCurrentDirectory(),
		"wwwroot",
		attachment.FilePath.TrimStart('/'));

	if (System.IO.File.Exists(physicalPath))
	{
		System.IO.File.Delete(physicalPath);
	}

	this._dbContext.Attachments.Remove(attachment);
	this._dbContext.SaveChanges();

	return Json(new { success = true });
}
```

JavaScript:

```html
<script type="text/javascript">
	function deleteAttachment(id) {
		$.ajax({
			url: "@Url.Action("DeleteAttachment", "Quiz")",
			method: "POST",
			data: { id: id },
			success: function () {
				loadAttachments();
			}
		});
	}
</script>
```

## Autentikacija i autorizacija

Autentikacija i autorizacija rješavaju dva različita problema.

Autentikacija odgovara na pitanje:

> Tko je korisnik?
> 

Autorizacija odgovara na pitanje:

> Smije li taj korisnik napraviti ovu akciju?
> 

Primjeri:

- korisnik se prijavljuje emailom i lozinkom — autentikacija
- samo admin smije brisati kvizove — autorizacija
- samo prijavljeni korisnici smiju uređivati podatke — autorizacija
- Google login potvrđuje identitet korisnika — autentikacija

## Autentikacija

ASP.NET Core ima ugrađeni Identity sustav za lokalnu registraciju, prijavu, odjavu, reset lozinke, korisnike, role i vanjske login providere. Kod kreiranja nove MVC aplikacije moguće je odabrati `Individual Accounts`. Time Visual Studio generira osnovnu konfiguraciju za Identity. Za postojeću aplikaciju potrebno je ručno uskladiti projekt.

Za autentikaciju se ne preporučuje pisati vlastiti sustav od nule. [ASP.NET](http://ASP.NET) Core Identity već rješava hashiranje lozinki, salt, lockout, reset lozinke, korisnike, role, claimove i 2FA integracije. Budući da je riječ o široko korištenom frameworku, sigurnosni problemi se brže pronalaze i ispravljaju nego u vlastitom ručno pisanom rješenju.

Osnovni koraci:

1. U Model projekt dodati `AppUser` klasu koja nasljeđuje `IdentityUser`
2. `QuizManagerDbContext` treba naslijediti `IdentityDbContext<AppUser>`
3. Instalirati potrebne NuGet pakete
4. Pokrenuti migracije
5. Uskladiti `Program.cs`
6. Uočiti i po potrebi generirati `Areas/Identity` datoteke
7. Uskladiti `_Layout.cshtml` i `_LoginPartial.cshtml`

### AppUser

Primjer:

`AppUser.cs`

```csharp
using Microsoft.AspNetCore.Identity;

public class AppUser : IdentityUser
{
}
```

### DbContext

Primjer:

`QuizManagerDbContext.cs`

```csharp
public class QuizManagerDbContext : IdentityDbContext<AppUser>
{
	public QuizManagerDbContext(DbContextOptions<QuizManagerDbContext> options)
		: base(options)
	{
	}

	public DbSet<Quiz> Quizzes { get; set; }
	public DbSet<Category> Categories { get; set; }
}
```

Potrebni paketi ovise o strukturi projekata, ali tipično uključuju:

- `Microsoft.AspNetCore.Identity.EntityFrameworkCore`
- `Microsoft.Extensions.Identity.Stores`
- `Microsoft.AspNetCore.Identity.UI`

### Program.cs

Konfiguracija se razlikuje ovisno o verziji projekta, ali osnovna ideja je registrirati Identity i uključiti autentikacijski middleware.

Primjer:

```csharp
builder.Services
	.AddDefaultIdentity<AppUser>(options =>
	{
		options.SignIn.RequireConfirmedAccount = false;
	})
	.AddRoles<IdentityRole>()
	.AddEntityFrameworkStores<QuizManagerDbContext>();

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();
```

U pipeline treba dodati:

```csharp
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
	name: "default",
	pattern: "{controller=Home}/{action=Index}/{id?}");

app.MapRazorPages();
```

<aside>
⚠️

`app.UseAuthentication()` mora biti prije `app.UseAuthorization()`. Ako redoslijed nije ispravan, autorizacija neće raditi očekivano.

</aside>

### Login partial

Ako se koristi vlastita `AppUser` klasa, treba provjeriti gdje se u generiranom kodu koristi `IdentityUser`. Ta mjesta treba zamijeniti s `AppUser`.

Posebno provjeriti:

- `_LoginPartial.cshtml`
- `Register.cshtml.cs`
- `ExternalLogin.cshtml.cs`
- eventualne Identity stranice koje su scaffoldane

<aside>
💡

Najlakši način za provjeru konfiguracije je kreirati novu praznu [ASP.NET](http://ASP.NET) Core MVC aplikaciju s odabranom opcijom `Individual Accounts`, a zatim usporediti razlike u `Program.cs`, layoutu i Identity datotekama.

</aside>

## OAuth

OAuth omogućuje prijavu preko vanjskih servisa poput Googlea, Facebooka, GitHuba ili Microsofta. U ovoj vježbi dovoljno je omogućiti jedan vanjski login provider, primjerice Google ili Facebook.

Za vanjsku prijavu potrebno je:

- omogućiti HTTPS
- kreirati aplikaciju na Google ili Facebook developers portalu
- dobiti `ClientId`
- dobiti `ClientSecret`
- konfigurirati provider u aplikaciji
- testirati registraciju i prijavu

### Pojednostavljeni OAuth flow

OAuth flow omogućuje da aplikacija ne mora sama provjeravati korisnikovu lozinku kod vanjskog providera.

Pojednostavljeni tijek:

1. Korisnik klikne “Login with Google” ili “Login with Microsoft”
2. Aplikacija preusmjeri korisnika na login stranicu providera
3. Korisnik se prijavi kod providera, ne u našoj aplikaciji
4. Provider vraća korisnika natrag na callback URL aplikacije s `authorization code` vrijednošću
5. Aplikacija server-to-server pozivom provjerava taj code kod providera
6. Ako je code valjan, aplikacija kreira lokalnu prijavu, najčešće authentication cookie

`ClientId` identificira aplikaciju kod providera. `ClientSecret` je tajna vrijednost kojom aplikacija dokazuje da smije verificirati authorization code.

`Scope` određuje koje podatke aplikacija traži od korisnika, primjerice email ili ime. Treba tražiti samo ono što aplikaciji stvarno treba. Što je scope veći, korisnik će teže prihvatiti consent screen.

### Postavljanje SSL-a

Vanjski login provideri zahtijevaju HTTPS. U development okruženju Visual Studio najčešće automatski konfigurira HTTPS, ali treba provjeriti `launchSettings.json`.

`Properties/launchSettings.json`

```json
{
	"profiles": {
		"https": {
			"applicationUrl": "https://localhost:7001;http://localhost:5001"
		}
	}
}
```

### Google login

Primjer konfiguracije:

```csharp
builder.Services
	.AddAuthentication()
	.AddGoogle(options =>
	{
		options.ClientId = builder.Configuration["Authentication:Google:ClientId"];
		options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
	});
```

Tajne podatke ne treba hardkodirati u kod. U developmentu ih je bolje spremiti u user secrets.

```bash
dotnet user-secrets set "Authentication:Google:ClientId" "..."
dotnet user-secrets set "Authentication:Google:ClientSecret" "..."
```

<aside>
🔐

Prave secret vrijednosti ne smiju se hardkodirati u kod niti commitati u javni repository. U developmentu je praktično koristiti user secrets, a u produkciji environment varijable, secret manager, credential store ili vault. Rotacija procurjelog ključa može imati posljedice, zato je bolje spriječiti curenje nego ga kasnije popravljati.

</aside>

### Facebook login

Primjer konfiguracije:

```csharp
builder.Services
	.AddAuthentication()
	.AddFacebook(options =>
	{
		options.AppId = builder.Configuration["Authentication:Facebook:AppId"];
		options.AppSecret = builder.Configuration["Authentication:Facebook:AppSecret"];
	});
```

### Passkey

Passkey je moderni način prijave koji koristi private/public key princip. Privatni ključ ostaje na korisnikovom uređaju, a provider ima javni ključ kojim može provjeriti potpisani login zahtjev.

Korisnik se može prijaviti biometrijom, PIN-om ili drugim lokalnim mehanizmom uređaja, ali privatni ključ ne napušta uređaj. Zato je passkey sigurniji i praktičniji od klasične lozinke, posebno za aplikacije koje žele smanjiti ovisnost o passwordima.

## Proširenje osnovne AppUser tablice

Osnovna `AspNetUsers` tablica često nije dovoljna. Aplikacija može trebati dodatne podatke o korisniku, primjerice OIB, JMBG, JMBAG ili interni identifikator.

U ovoj vježbi `AppUser` treba proširiti poljima:

- `OIB`
- `JMBG`

Primjer:

```csharp
public class AppUser : IdentityUser
{
	[Required]
	[StringLength(11, MinimumLength = 11)]
	[RegularExpression("^[0-9]*$")]
	public string OIB { get; set; }

	[Required]
	[StringLength(13, MinimumLength = 13)]
	[RegularExpression("^[0-9]*$")]
	public string JMBG { get; set; }
}
```

Nakon izmjene klase potrebno je napraviti migraciju:

```bash
dotnet ef migrations add ExtendAppUserWithOibAndJmbg
dotnet ef database update
```

<aside>
⚠️

Prije primjene migracije provjeriti što je generirano. Ako migracija generira neočekivane promjene nad drugim Identity poljima, te promjene treba razumjeti ili ukloniti prije `database update`.

</aside>

### Scaffold Identity stranica

Identity UI se može generirati kroz Visual Studio:

1. Desni klik na Web projekt → `Add`  → `New Scaffolded Item`  → odabrati `Identity`
2. Odabrati postojeći layout ili ostaviti prazno ako se koristi Razor `_ViewStart`
3. Odabrati stranice:
    - `Account/Register`
    - `Account/ExternalLogin`
4. Odabrati postojeći `QuizManagerDbContext`

Mogući problemi:

- scaffolder može generirati duplikat `QuizManagerDbContext` klase u `Identity/Data` folderu
    - taj duplikat treba obrisati
- Visual Studio može javiti grešku zbog verzije code generation paketa
    - provjeriti verziju paketa `Microsoft.VisualStudio.Web.CodeGeneration.Design`

### Register forma

Nakon scaffoldanja treba modificirati:

- `Register.cshtml`
- `Register.cshtml.cs`

U input model dodati polja:

```csharp
[Required]
[StringLength(11, MinimumLength = 11)]
[RegularExpression("^[0-9]*$", ErrorMessage = "OIB smije sadržavati samo brojeve.")]
[Display(Name = "OIB")]
public string OIB { get; set; }

[Required]
[StringLength(13, MinimumLength = 13)]
[RegularExpression("^[0-9]*$", ErrorMessage = "JMBG smije sadržavati samo brojeve.")]
[Display(Name = "JMBG")]
public string JMBG { get; set; }
```

Kod kreiranja korisnika postaviti vrijednosti:

```csharp
var user = new AppUser
{
	UserName = Input.Email,
	Email = Input.Email,
	OIB = Input.OIB,
	JMBG = Input.JMBG
};
```

U `Register.cshtml` dodati polja:

```html
<div class="form-floating mb-3">
	<input asp-for="Input.OIB" class="form-control" />
	<label asp-for="Input.OIB"></label>
	<span asp-validation-for="Input.OIB" class="text-danger"></span>
</div>

<div class="form-floating mb-3">
	<input asp-for="Input.JMBG" class="form-control" />
	<label asp-for="Input.JMBG"></label>
	<span asp-validation-for="Input.JMBG" class="text-danger"></span>
</div>
```

### ExternalLogin forma

Kod prve prijave vanjskim servisom korisnik često mora potvrditi email i dovršiti registraciju. Ta forma je u:

```
Areas/Identity/Pages/Account/ExternalLogin.cshtml
```

I pripadajući page model:

```
Areas/Identity/Pages/Account/ExternalLogin.cshtml.cs
```

Treba napraviti iste promjene:

- dodati `OIB`
- dodati `JMBG`
- dodati validaciju
- prikazati polja u formi
- kod kreiranja `AppUser` objekta spremiti vrijednosti

## Autorizacija korisnika

Nakon što znamo tko je korisnik, možemo ograničiti što smije raditi. U [ASP.NET](http://ASP.NET) Core MVC-u za to se koristi atribut `[Authorize]`.

Primjer na razini akcije:

```csharp
[Authorize]
public IActionResult Create()
{
	return View();
}
```

Primjer na razini controllera:

```csharp
[Authorize]
public class QuizController : Controller
{
	public IActionResult Index()
	{
		return View();
	}
}
```

Ako je controller zaštićen, ali neka akcija treba biti javna, koristi se `[AllowAnonymous]`:

```csharp
[AllowAnonymous]
public IActionResult Index()
{
	return View();
}
```

### Pravila za QuizController

Za ovu vježbu:

- svi mogu pregledavati listu kvizova i koristiti pretragu
- svi mogu pregledavati detalje kviza
- samo prijavljeni korisnici mogu kreirati, uređivati i brisati
- kasnije se dodatno uvode role za precizniju kontrolu

Primjer:

```csharp
public class QuizController : BaseController
{
	[AllowAnonymous]
	public IActionResult Index()
	{
		return View();
	}

	[AllowAnonymous]
	public IActionResult Details(int id)
	{
		return View();
	}

	[Authorize]
	public IActionResult Create()
	{
		return View();
	}

	[Authorize]
	[HttpPost]
	public IActionResult Create(Quiz model)
	{
		// ...
	}

	[Authorize]
	public IActionResult Edit(int id)
	{
		return View();
	}

	[Authorize]
	[HttpPost]
	public IActionResult Edit(Quiz model)
	{
		// ...
	}

	[Authorize]
	public IActionResult Delete(int id)
	{
		// ...
	}
}
```

## Informacije o trenutnom korisniku

Često je potrebno znati koji je korisnik napravio neku promjenu. Primjer:

- tko je kreirao kviz
- tko je zadnji izmijenio kviz
- kome pripada zapis
- koja pravila vrijede za tog korisnika

U controlleru se može dohvatiti ID trenutnog korisnika preko `UserManager<AppUser>`:

```csharp
private readonly UserManager<AppUser> _userManager;

public QuizController(
	QuizManagerDbContext dbContext,
	UserManager<AppUser> userManager)
{
	this._dbContext = dbContext;
	this._userManager = userManager;
}

public IActionResult Index()
{
	var userId = this._userManager.GetUserId(base.User);

	return View();
}
```

Bolje je takav kod izdvojiti u bazni controller.

### BaseController

Primjer:

```csharp
public abstract class BaseController : Controller
{
	protected readonly UserManager<AppUser> UserManager;

	protected BaseController(UserManager<AppUser> userManager)
	{
		this.UserManager = userManager;
	}

	protected string UserId
	{
		get
		{
			return this.UserManager.GetUserId(this.User);
		}
	}
}
```

`QuizController` zatim nasljeđuje `BaseController`:

```csharp
public class QuizController : BaseController
{
	private readonly QuizManagerDbContext _dbContext;

	public QuizController(
		QuizManagerDbContext dbContext,
		UserManager<AppUser> userManager)
		: base(userManager)
	{
		this._dbContext = dbContext;
	}
}
```

## Autorizacija po ulogama

Autorizacija po ulogama omogućuje da različiti prijavljeni korisnici imaju različita prava. Primjer:

- `Admin` smije brisati
- `Manager` smije uređivati
- obični korisnik smije samo pregledavati detalje
- anonimni korisnik smije vidjeti samo listu

Da bi role radile, potrebno je:

- omogućiti role u Identity konfiguraciji
- kreirati role u bazi
- dodijeliti korisnike u role
- označiti akcije odgovarajućim `[Authorize(Roles = "...")]` atributima

### Omogućavanje rola

U `Program.cs` treba uključiti role:

```csharp
builder.Services
	.AddDefaultIdentity<AppUser>(options =>
	{
		options.SignIn.RequireConfirmedAccount = false;
	})
	.AddRoles<IdentityRole>()
	.AddEntityFrameworkStores<QuizManagerDbContext>();
```

Ako se koristi `AddIdentity`, konfiguracija može izgledati drugačije, ali ideja je ista: Identity mora znati koristiti `IdentityRole`.

### Seed rola

Role se mogu dodati ručno u bazu, ali je bolje napraviti seed.

Primjer:

```csharp
public static async Task SeedRoles(IServiceProvider serviceProvider)
{
	var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();

	string[] roles = { "Admin", "Manager" };

	foreach (var role in roles)
	{
		if (!await roleManager.RoleExistsAsync(role))
		{
			await roleManager.CreateAsync(new IdentityRole(role));
		}
	}
}
```

Dodjela role korisniku može se napraviti ručno u bazi za potrebe vježbe ili kroz privremeni seed.

<aside>
⚠️

Nakon dodavanja korisnika u rolu potrebno je napraviti logout i ponovno login. Role se nalaze u korisničkim claimovima i često se neće vidjeti dok se prijava ne osvježi.

</aside>

### Autorizacijske anotacije

Primjeri:

```csharp
[Authorize(Roles = "Admin")]
public IActionResult Delete(int id)
{
	// ...
}
```

```csharp
[Authorize(Roles = "Admin,Manager")]
public IActionResult Edit(int id)
{
	// ...
}
```

```csharp
[Authorize]
public IActionResult Details(int id)
{
	// ...
}
```

```csharp
[AllowAnonymous]
public IActionResult Index()
{
	// ...
}
```

### Pravila za vježbu

Za `QuizController` treba vrijediti:

| Akcija | Pristup |
| --- | --- |
| `Index` i pretraga | Svi korisnici, uključujući anonimne |
| `Details` | Bilo koji prijavljeni korisnik |
| `Create` | `Admin` ili `Manager` |
| `Edit` | `Admin` ili `Manager` |
| `Delete` | Samo `Admin` |

Primjer:

```csharp
public class QuizController : BaseController
{
	[AllowAnonymous]
	public IActionResult Index()
	{
		return View();
	}

	[Authorize]
	public IActionResult Details(int id)
	{
		return View();
	}

	[Authorize(Roles = "Admin,Manager")]
	public IActionResult Create()
	{
		return View();
	}

	[Authorize(Roles = "Admin,Manager")]
	[HttpPost]
	public IActionResult Create(Quiz model)
	{
		// ...
	}

	[Authorize(Roles = "Admin,Manager")]
	public IActionResult Edit(int id)
	{
		return View();
	}

	[Authorize(Roles = "Admin,Manager")]
	[HttpPost]
	public IActionResult Edit(int id, Quiz model)
	{
		// ...
	}

	[Authorize(Roles = "Admin")]
	public IActionResult Delete(int id)
	{
		return View();
	}

	[Authorize(Roles = "Admin")]
	[HttpPost]
	public IActionResult DeleteConfirmed(int id)
	{
		// ...
	}
}
```

### Role vs permission

Role su dobre kada aplikacija ima mali broj jasnih tipova korisnika, primjerice `Admin`, `Manager`, `Student` ili `Professor`.

Ako sustav počne imati mnogo sitnih pravila, role mogu eksplodirati u previše kombinacija. Tada se često uvode permissioni ili privilegei, primjerice:

- `tasks.edit`
- `quiz.delete`
- `users.manage`

Role odgovaraju na pitanje kojoj skupini korisnik pripada. Permissioni odgovaraju na pitanje smije li korisnik napraviti konkretnu akciju.

Za ovu vježbu role su dovoljne. U većim sustavima permissioni daju finiju kontrolu, ali zahtijevaju dodatnu infrastrukturu, primjerice custom authorization attribute ili policy.