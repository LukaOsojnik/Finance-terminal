using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Services;

namespace simple_bloomberg_terminal.Controllers;

// The signed-in user's own account page: view profile + upload/replace/delete a single profile
// picture. [Authorize] (no roles) = any authenticated user can manage their own picture.
[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly IWebHostEnvironment _env;
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;

    // Only real image types are accepted; keep extension + content-type in lockstep so a renamed
    // file can't slip through.
    private static readonly Dictionary<string, string> AllowedTypes = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/gif"] = ".gif",
        ["image/webp"] = ".webp",
    };
    private const long MaxBytes = 5 * 1024 * 1024; // 5 MB

    public AccountController(UserManager<AppUser> users, IWebHostEnvironment env,
        AppDbContext db, IDataProtectionProvider dp)
    {
        _users = users;
        _env = env;
        _db = db;
        _protector = dp.CreateProtector(UserApiKeyProvider.Purpose);
    }

    [HttpGet]
    public async Task<IActionResult> Profile()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(user);
    }

    // AJAX: current picture metadata, consumed by the Dropzone page to render the existing file.
    // Returns an empty payload (not 404) when there's no picture so the JS can just show the dropzone.
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

    // Dropzone posts one file under the field name "file". Saves it to
    // wwwroot/uploads/profiles/{userId}/{guid}{ext}, records metadata on AppUser, and replaces any
    // existing picture (single-file model). Returns JSON for the async UI.
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

        // Remove the previous picture before writing the new one — one file per user.
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

    // ── Bring-your-own API keys ───────────────────────────────────────────────────────────────────
    // The signed-in user's own DeepSeek / FMP / Perplexity keys, stored encrypted (Data Protection)
    // in UserApiKeys. The page shows only "set · ••••last4" status, never the raw key.
    [HttpGet]
    public async Task<IActionResult> ApiKeys()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var row = await _db.UserApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == user.Id);
        return View(new ApiKeysViewModel
        {
            DeepSeek = Status(row?.DeepSeekKey),
            Fmp = Status(row?.FmpKey),
            Perplexity = Status(row?.PerplexityKey)
        });
    }

    // Per key: a "clear" tick removes it; a non-blank input sets it (encrypted); blank leaves it as-is
    // (so the user can update one key without re-typing the others).
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApiKeys(
        string? deepSeekKey, string? fmpKey, string? perplexityKey,
        bool clearDeepSeek = false, bool clearFmp = false, bool clearPerplexity = false)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var row = await _db.UserApiKeys.FirstOrDefaultAsync(k => k.UserId == user.Id);
        if (row is null)
        {
            row = new UserApiKey { UserId = user.Id };
            _db.UserApiKeys.Add(row);
        }

        row.DeepSeekKey = Apply(row.DeepSeekKey, deepSeekKey, clearDeepSeek);
        row.FmpKey = Apply(row.FmpKey, fmpKey, clearFmp);
        row.PerplexityKey = Apply(row.PerplexityKey, perplexityKey, clearPerplexity);

        await _db.SaveChangesAsync();
        TempData["ApiKeysSaved"] = "Your API keys have been updated.";
        return RedirectToAction(nameof(ApiKeys));
    }

    // Decide a column's new ciphertext: clear -> null; new value -> encrypt; otherwise keep existing.
    private string? Apply(string? current, string? input, bool clear)
    {
        if (clear) return null;
        if (string.IsNullOrWhiteSpace(input)) return current;
        return _protector.Protect(input.Trim());
    }

    // Build the masked display status for one stored (ciphertext) key.
    private ApiKeyStatus Status(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return new ApiKeyStatus { IsSet = false };
        string? plain = null;
        try { plain = _protector.Unprotect(cipher); }
        catch (System.Security.Cryptography.CryptographicException) { /* unreadable -> treat as set, no last4 */ }
        return new ApiKeyStatus
        {
            IsSet = true,
            Last4 = plain is { Length: >= 4 } ? plain[^4..] : null
        };
    }

    // Map a stored web path (/uploads/...) back to disk and delete it if present. No-op for null.
    private void DeletePhysicalFile(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath)) return;
        var full = Path.Combine(_env.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
