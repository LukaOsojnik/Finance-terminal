using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Controllers;

// The signed-in user's own account page: view profile + upload/replace/delete a single profile
// picture. [Authorize] (no roles) = any authenticated user can manage their own picture.
[Authorize]
public class AccountController : Controller
{
    private readonly UserManager<AppUser> _users;
    private readonly IWebHostEnvironment _env;

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

    // Map a stored web path (/uploads/...) back to disk and delete it if present. No-op for null.
    private void DeletePhysicalFile(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath)) return;
        var full = Path.Combine(_env.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
    }
}
