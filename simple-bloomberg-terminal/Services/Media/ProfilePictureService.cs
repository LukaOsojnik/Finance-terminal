namespace simple_bloomberg_terminal.Services.Media;

/// <summary>
/// Validates profile-picture uploads against the allowed image types / size limit from config
/// ("ProfilePicture"). The image bytes themselves are stored on the AppUser DB row (see
/// AccountController), so there is no filesystem access here.
/// </summary>
public class ProfilePictureService
{
    private readonly Dictionary<string, string> _allowedTypes;
    private readonly long _maxBytes;

    public ProfilePictureService(IConfiguration config)
    {
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
}
