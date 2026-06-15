namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Stores a user's single profile picture under wwwroot/uploads/profiles/{userId}/ and validates
/// uploads against the allowed image types / size limit from config ("ProfilePicture"). All
/// File/Directory access lives here; the controller deals only in the resulting web path + metadata.
/// </summary>
public class ProfilePictureService
{
    private readonly IWebHostEnvironment _env;
    private readonly Dictionary<string, string> _allowedTypes;
    private readonly long _maxBytes;

    public ProfilePictureService(IWebHostEnvironment env, IConfiguration config)
    {
        _env = env;
        var section = config.GetSection("ProfilePicture");
        _allowedTypes = section.GetSection("AllowedTypes").Get<Dictionary<string, string>>()
            ?? new Dictionary<string, string>();
        _maxBytes = section.GetValue<long>("MaxBytes");
    }

    // Validate an upload: returns the matching extension, or an error message to surface as 400.
    // Keeps extension + content-type in lockstep so a renamed file can't slip through.
    public (string? ext, string? error) Validate(IFormFile? file)
    {
        if (file is null || file.Length == 0) return (null, "No file received.");
        if (file.Length > _maxBytes) return (null, $"File exceeds the {_maxBytes / (1024 * 1024)} MB limit.");
        if (!_allowedTypes.TryGetValue(file.ContentType, out var ext))
            return (null, "Only JPEG, PNG, GIF, or WebP images are allowed.");
        return (ext, null);
    }

    // Replace any existing picture with the new file (one file per user); returns the stored web path.
    public async Task<string> SaveAsync(string userId, IFormFile file, string ext, string? previousPath)
    {
        Delete(previousPath);
        var userDir = Path.Combine(_env.WebRootPath, "uploads", "profiles", userId);
        Directory.CreateDirectory(userDir);
        var fileName = $"{Guid.NewGuid():N}{ext}";
        await using var stream = File.Create(Path.Combine(userDir, fileName));
        await file.CopyToAsync(stream);
        return $"/uploads/profiles/{userId}/{fileName}";
    }

    // Map a stored web path (/uploads/...) back to disk and delete it if present. No-op for null.
    public void Delete(string? webPath)
    {
        if (string.IsNullOrEmpty(webPath)) return;
        var full = Path.Combine(_env.WebRootPath, webPath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full)) File.Delete(full);
    }
}
