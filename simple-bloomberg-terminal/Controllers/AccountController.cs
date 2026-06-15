using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    private readonly IUserApiKeyProvider _keys;
    private readonly ProfilePictureService _pictures;

    public AccountController(UserManager<AppUser> users, IUserApiKeyProvider keys,
        ProfilePictureService pictures)
    {
        _users = users;
        _keys = keys;
        _pictures = pictures;
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

    // Dropzone posts one file under the field name "file". ProfilePictureService validates it, replaces
    // any existing picture, and returns the stored web path; we record the metadata on AppUser.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UploadProfilePicture(IFormFile file)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var (ext, error) = _pictures.Validate(file);
        if (error is not null) return BadRequest(error);

        user.ProfilePicturePath = await _pictures.SaveAsync(user.Id, file, ext!, user.ProfilePicturePath);
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

        _pictures.Delete(user.ProfilePicturePath);
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
    // in UserApiKeys by IUserApiKeyProvider. The page shows only "set · ••••last4" status, never the
    // raw key.
    [HttpGet]
    public async Task<IActionResult> ApiKeys()
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        var keys = await _keys.GetAsync();
        return View(new ApiKeysViewModel
        {
            DeepSeek = Status(keys.DeepSeek),
            Fmp = Status(keys.Fmp),
            Perplexity = Status(keys.Perplexity)
        });
    }

    // Per key: a "clear" tick removes it; a non-blank input sets it; blank leaves it as-is (so the user
    // can update one key without re-typing the others). Encryption + persistence live in the provider.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveApiKeys(
        string? deepSeekKey, string? fmpKey, string? perplexityKey,
        bool clearDeepSeek = false, bool clearFmp = false, bool clearPerplexity = false)
    {
        var user = await _users.GetUserAsync(User);
        if (user is null) return Challenge();

        await _keys.SaveAsync(new ApiKeyEdit(
            deepSeekKey, clearDeepSeek, fmpKey, clearFmp, perplexityKey, clearPerplexity));

        TempData["ApiKeysSaved"] = "Your API keys have been updated.";
        return RedirectToAction(nameof(ApiKeys));
    }

    // Build the masked display status from a decrypted key (null/blank = not set). An undecryptable
    // key resolves to null upstream, so it correctly shows as not set — matching how the keyed clients
    // treat it.
    private static ApiKeyStatus Status(string? plain) =>
        string.IsNullOrEmpty(plain)
            ? new ApiKeyStatus { IsSet = false }
            : new ApiKeyStatus { IsSet = true, Last4 = plain.Length >= 4 ? plain[^4..] : null };
}
