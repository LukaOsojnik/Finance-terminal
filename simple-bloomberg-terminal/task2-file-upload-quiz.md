# Task 2: File Upload — kako je implementirano i zašto

**Projekt:** simple-bloomberg-terminal (ASP.NET Core MVC + EF Core + Identity)
**Status:** Implementiran je kompletan async upload pattern za **profile picture** (1 slika po useru). Quiz/Attachment dio iz lab specifikacije (1 kviz -> N priloga) **nije implementiran i nije potreban** za ovaj projekt — pattern ispod je generička baza koja bi se mogla proširiti na N:1 slučaj kasnije, ali to nije cilj ove vježbe.

---

## 1. Arhitektura — pregled toka

```
Browser (Profile.cshtml)
   |
   |  1. GET /Account/Profile  -> server renderira AppUser model, partial _ProfilePicture
   |
   |  2. Drag & drop slike u Dropzone widget
   v
Dropzone JS
   |  3. POST multipart/form-data -> /Account/UploadProfilePicture (async, bez reload)
   v
AccountController.UploadProfilePicture
   |  4. validacija (size, content-type)
   |  5. obriši stari fajl s diska (DeletePhysicalFile)
   |  6. spremi novi fajl na disk (wwwroot/uploads/profiles/{userId}/{guid}.ext)
   |  7. update 5 kolona na AppUser, UserManager.UpdateAsync -> EF SaveChanges
   v
   8. return JSON { path, name, size }
   |
   v
Dropzone "success" callback -> renderPicture() ažurira DOM bez reload-a
```

Brisanje ide istim putem, samo preko `DeleteProfilePicture` + `fetch()`.

---

## 2. Entity: `Models/Entities/AppUser.cs`

Umjesto posebne `Attachment` tablice (1 kviz -> N priloga), ovdje je slika **1:1** s userom — pa metapodaci žive direktno na `AppUser` kao 5 nullable kolona.

```csharp
using Microsoft.AspNetCore.Identity;

namespace simple_bloomberg_terminal.Models.Entities;

public class AppUser : IdentityUser
{
    // Web-relative path do spremljene slike, npr. "/uploads/profiles/{userId}/{guid}.png".
    // Null = nema slike (prikazuje se placeholder).
    public string? ProfilePicturePath { get; set; }

    // Metapodaci uhvaćeni u trenutku uploada.
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}
```

**Zašto ovako:**
- `IdentityUser` već daje `Id`, `UserName`, `Email`, `PasswordHash` — dodajemo samo upload-specifične kolone.
- Sve kolone su `nullable` (`?`) — to je "nema slike" stanje, bez potrebe za posebnim flagom.
- Path je **web-relative** (`/uploads/...`), ne apsolutni disk path — tako se direktno koristi u `<img src="...">` i ne curi info o serverskom file sistemu.

**Za N:1 slučaj (kviz -> N priloga)** bi ovo umjesto kolona na `AppUser` bila posebna tablica:
```csharp
public class Attachment
{
    public int Id { get; set; }
    public int QuizId { get; set; }
    public Quiz Quiz { get; set; }
    public string FilePath { get; set; }
    public string OriginalFileName { get; set; }
    public string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
}
```
(Nije implementirano — naveden samo kao referenca za pattern, ako zatreba.)

---

## 3. Controller: `Controllers/AccountController.cs`

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Controllers;

[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly IWebHostEnvironment _env;

    // Mapiranje content-type -> extenzija. Extenzija na disku se izvodi iz
    // VALIDIRANOG content-type-a, ne iz klijentskog filename-a -- sprječava
    // "evil.php" preimenovan u "evil.jpg" da završi kao .php na disku.
    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public AccountController(UserManager<AppUser> users, IWebHostEnvironment env)
    {
        _users = users;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(user);
    }

    // AJAX: trenutna slika kao JSON. Postoji kao endpoint, ali Profile.cshtml
    // ga trenutno NE zove -- initial render ide server-side preko Model-a
    // (vidi sekciju 4). Ostavljen kao primjer "popis datoteka preko AJAX-a" patterna.
    [HttpGet]
    public async Task<IActionResult> CurrentPicture()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.ProfilePicturePath is null) return Json(new { hasPicture = false });
        return Json(new
        {
            hasPicture = true,
            path = user.ProfilePicturePath,
            name = user.OriginalFileName,
            contentType = user.ContentType,
            size = user.SizeBytes,
            uploadedAt = user.UploadedAt
        });
    }

    // Dropzone POST-a jedan fajl pod field name "file".
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        if (file is null || file.Length == 0)
            return BadRequest("No file received.");
        if (file.Length > MaxBytes)
            return BadRequest("File exceeds the 5 MB limit.");
        if (!AllowedTypes.TryGetValue(file.ContentType, out var ext))
            return BadRequest("Only JPEG, PNG, GIF, or WebP images are allowed.");

        // Ukloni staru sliku PRIJE pisanja nove -- jedan fajl po useru.
        DeletePhysicalFile(user.ProfilePicturePath);

        var userDir = Path.Combine(_env.WebRootPath, "uploads", "profiles", user.Id);
        Directory.CreateDirectory(userDir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        var fullPath = Path.Combine(userDir, fileName);
        await using (var stream = System.IO.File.Create(fullPath))
            await file.CopyToAsync(stream);

        user.ProfilePicturePath = $"/uploads/profiles/{user.Id}/{fileName}";
        user.OriginalFileName = file.FileName;
        user.ContentType = file.ContentType;
        user.SizeBytes = file.Length;
        user.UploadedAt = DateTime.UtcNow;
        await _users.UpdateAsync(user);

        return Json(new { path = user.ProfilePicturePath, name = user.OriginalFileName, size = user.SizeBytes });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteProfilePicture()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        DeletePhysicalFile(user.ProfilePicturePath);
        user.ProfilePicturePath = null;
        user.OriginalFileName = null;
        user.ContentType = null;
        user.SizeBytes = null;
        user.UploadedAt = null;
        await _users.UpdateAsync(user);

        return Ok();
    }

    // Mapira web path (/uploads/...) na disk path i briše ako postoji. No-op za null.
    private void DeletePhysicalFile(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath)) return;
        var full = Path.Combine(_env.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
```

### Objašnjenje ključnih dijelova

| Dio | Zašto |
|-----|-------|
| `[ValidateAntiForgeryToken]` na POST akcijama | Standardna CSRF zaštita. Dropzone mora ručno poslati token kao header (vidi sekciju 4). |
| `AllowedTypes` dict | Server-side whitelist. Dropzone ima i client-side `acceptedFiles`, ali to je samo UX hint — pravu provjeru radi server. |
| `Directory.CreateDirectory(userDir)` | Idempotentno — ne baca ako direktorij već postoji. Kreira `uploads/profiles/{userId}/` po potrebi. |
| `Guid.NewGuid():N` za filename | Sprječava collision i path traversal preko originalnog filename-a. `:N` format = bez crtica (32 hex znaka). |
| `await using (var stream = ...)` | `IAsyncDisposable` — stream se zatvori automatski čak i kod exceptiona. |
| `UserManager<AppUser>.UpdateAsync` | Identity-ov wrapper oko `DbContext.SaveChanges` — AppUser je Identity entitet, pa se update radi kroz UserManager, ne kroz raw `DbContext`. |

---

## 4. View: `Views/Account/Profile.cshtml`

```cshtml
@model simple_bloomberg_terminal.Models.Entities.AppUser
@{
    ViewData["Title"] = "Profile";
}

<h2>Profile</h2>
<p class="text-muted">@Model.Email</p>

<div class="row">
    <div class="col-md-4">
        <h5>Current picture</h5>
        <div id="currentPicture">
            <partial name="_ProfilePicture" model="Model"/>
        </div>
        <dl id="pictureMeta" class="mt-2 small" style="@(Model.ProfilePicturePath is null ? "display:none" : "")">
            <dt>File</dt><dd id="metaName">@Model.OriginalFileName</dd>
            <dt>Type</dt><dd id="metaType">@Model.ContentType</dd>
            <dt>Size</dt><dd id="metaSize">@(Model.SizeBytes is { } b ? $"{b / 1024.0:F1} KB" : "")</dd>
            <dt>Uploaded</dt><dd id="metaDate">@Model.UploadedAt?.ToString("u")</dd>
        </dl>
        <button id="deleteBtn" type="button" class="btn btn-sm btn-outline-danger mt-2"
                style="@(Model.ProfilePicturePath is null ? "display:none" : "")">Delete picture</button>
    </div>
    <div class="col-md-8">
        <h5>Upload a new picture</h5>
        <p class="small text-muted">JPEG, PNG, GIF or WebP, max 5 MB. A new upload replaces the current picture.</p>
        @Html.AntiForgeryToken()
        <form id="pictureDropzone" class="dropzone"
              action="@Url.Action("UploadProfilePicture", "Account")"></form>
    </div>
</div>

@section Scripts {
    <link rel="stylesheet" href="~/lib/dropzone/dropzone.min.css"/>
    <script src="~/lib/dropzone/dropzone.min.js"></script>
    <script>
        Dropzone.autoDiscover = false;
        const token = document.querySelector('input[name="__RequestVerificationToken"]').value;

        const dz = new Dropzone("#pictureDropzone", {
            paramName: "file",
            maxFiles: 1,
            maxFilesize: 5, // MB
            acceptedFiles: "image/jpeg,image/png,image/gif,image/webp",
            addRemoveLinks: false,
            dictDefaultMessage: "Drop an image here or click to upload",
            headers: { "RequestVerificationToken": token }
        });

        // Jedan fajl odjednom: ukloni stari preview prije dodavanja novog.
        dz.on("addedfile", function () {
            if (dz.files.length > 1) dz.removeFile(dz.files[0]);
        });

        dz.on("success", function (file, resp) {
            renderPicture(resp.path, resp.name, file.type, resp.size, new Date().toISOString());
            dz.removeAllFiles(true);
        });

        dz.on("error", function (file, msg) {
            alert(typeof msg === "string" ? msg : (msg.error || "Upload failed."));
            dz.removeFile(file);
        });

        document.getElementById("deleteBtn").addEventListener("click", async function () {
            if (!confirm("Delete your profile picture?")) return;
            const res = await fetch('@Url.Action("DeleteProfilePicture", "Account")', {
                method: "POST",
                headers: { "RequestVerificationToken": token }
            });
            if (res.ok) clearPicture();
            else alert("Delete failed.");
        });

        function renderPicture(path, name, type, size, uploadedAt) {
            document.getElementById("currentPicture").innerHTML =
                '<img src="' + path + '" alt="Profile picture" ' +
                'style="width:128px;height:128px;object-fit:cover;border-radius:8px;border:1px solid #333;"/>';
            document.getElementById("metaName").textContent = name || "";
            document.getElementById("metaType").textContent = type || "";
            document.getElementById("metaSize").textContent = size ? (size / 1024).toFixed(1) + " KB" : "";
            document.getElementById("metaDate").textContent = uploadedAt || "";
            document.getElementById("pictureMeta").style.display = "";
            document.getElementById("deleteBtn").style.display = "";
        }

        function clearPicture() {
            document.getElementById("currentPicture").innerHTML =
                '<div style="width:128px;height:128px;display:flex;align-items:center;justify-content:center;' +
                'border-radius:8px;border:1px dashed #555;color:#888;">No picture</div>';
            document.getElementById("pictureMeta").style.display = "none";
            document.getElementById("deleteBtn").style.display = "none";
        }
    </script>
}
```

### Objašnjenje ključnih dijelova

| Dio | Zašto |
|-----|-------|
| `Dropzone.autoDiscover = false` | Default Dropzone bi auto-inicijalizirao SVAKI element s klasom `.dropzone` na stranici. Mi ga ručno inicijaliziramo s konfiguracijom (maxFiles, acceptedFiles, headers), pa je autoDiscover isključen. |
| `headers: { "RequestVerificationToken": token }` | Dropzone radi svoj fetch/XHR mimo standardnog `<form>` submita, pa antiforgery token mora ručno ići kao HTTP header (controller ima `[ValidateAntiForgeryToken]` koji ga čita). |
| `dz.on("addedfile", ...)` | Implementira "max 1 file" UX — kad korisnik dropne novu sliku, stara se ukloni iz prikaza prije nego nova legne. |
| `dz.on("success", ...)` | Server vrati JSON `{path, name, size}` (vidi controller) -> `renderPicture()` updateira DOM bez reload-a stranice. |
| `dz.removeAllFiles(true)` | `true` = force, makni i fajlove koji su "in progress". Resetira dropzone widget na praznu state nakon uspješnog uploada. |
| Server-side render u `_ProfilePicture` partial | Initial state (kad stranica prvi put loada) ne ide AJAX-om — `View(user)` već ima sve podatke, partial ih odmah renderira. AJAX (`CurrentPicture`) bi trebao samo ako bi se slika mogla promijeniti BEZ reload-a stranice od strane nekog drugog izvora. |

---

## 5. Partial: `Views/Shared/_ProfilePicture.cshtml`

```cshtml
@model simple_bloomberg_terminal.Models.Entities.AppUser

@if (Model.ProfilePicturePath is not null)
{
    <img src="@Model.ProfilePicturePath" alt="Profile picture"
         style="width:128px;height:128px;object-fit:cover;border-radius:8px;border:1px solid var(--border, #333);"/>
}
else
{
    <div style="width:128px;height:128px;display:flex;align-items:center;justify-content:center;
                border-radius:8px;border:1px dashed var(--border, #555);color:var(--muted, #888);">
        No picture
    </div>
}
```

Čisti conditional render — partial postoji da se ista logika (slika ili placeholder) može reuse-ati i na drugim stranicama (npr. nav bar avatar), ne samo na Profile stranici.

---

## 6. Step-by-step: Upload

1. User otvori `/Account/Profile`. Server renderira `_ProfilePicture` partial + Dropzone form.
2. User dropne sliku u Dropzone widget.
3. **Client-side** provjera (Dropzone): tip u `acceptedFiles`, veličina <= `maxFilesize`. Ovo je samo UX — ne smije se vjerovati, server provjerava ponovo.
4. Dropzone šalje `POST /Account/UploadProfilePicture` kao `multipart/form-data`, polje `file`, header `RequestVerificationToken: <token>`.
5. **Server-side** provjera u `UploadProfilePicture`:
   - `file.Length == 0` -> 400
   - `file.Length > 5MB` -> 400
   - `file.ContentType` nije u `AllowedTypes` -> 400
6. Stari fajl (ako postoji) se briše s diska (`DeletePhysicalFile`).
7. Novi fajl se piše na `wwwroot/uploads/profiles/{userId}/{guid}{ext}`.
8. 5 kolona na `AppUser` se ažurira i `UpdateAsync` sprema u bazu.
9. Controller vrati `200 OK` + JSON `{ path, name, size }`.
10. Dropzone `success` handler zove `renderPicture(...)` -> DOM se ažurira bez reload-a. `removeAllFiles(true)` resetira widget.

## 7. Step-by-step: Delete

1. User klikne "Delete picture" -> `confirm()` dialog.
2. Ako potvrdi: `fetch(POST /Account/DeleteProfilePicture, header RequestVerificationToken)`.
3. Controller: `DeletePhysicalFile` briše fajl s diska, sve 5 kolona na `AppUser` se postave na `null`, `UpdateAsync`.
4. `200 OK` -> JS `clearPicture()` vrati placeholder + sakrije meta/delete button.

---

## 8. Mapping na checklist iz zadatka

| Zahtjev | Status | Gdje |
|---------|--------|------|
| Upload vezan uz konkretan entitet | ✅ (vezan uz `AppUser`, 1:1 — quiz/Attachment N:1 nije implementiran, vidi napomenu na vrhu) | `AppUser.cs` |
| Async upload preko Dropzone | ✅ | `Profile.cshtml` linije 41-65 |
| Datoteke spremljene na disk | ✅ | `UploadProfilePicture`, `wwwroot/uploads/profiles/{userId}/{guid}.ext` |
| U bazu spremljeni metapodaci i putanja | ✅ | `AppUser`: `ProfilePicturePath`, `OriginalFileName`, `ContentType`, `SizeBytes`, `UploadedAt` |
| Popis datoteka učitan AJAX pozivom | ⚠️ djelomično — `CurrentPicture()` endpoint postoji i vraća JSON, ali `Profile.cshtml` ga ne zove (initial render je server-side preko Model-a) | `AccountController.CurrentPicture` |
| Brisanje postojećih datoteka | ✅ | `DeleteProfilePicture` + `fetch()` u `Profile.cshtml` |

---

## 9. Static fajlovi

- `wwwroot/lib/dropzone/dropzone.min.js` — bundled Dropzone JS
- `wwwroot/lib/dropzone/dropzone.min.css` — bundled Dropzone CSS
- `wwwroot/uploads/profiles/` — runtime-generirani direktorij, sadrži slike po `{userId}/`
