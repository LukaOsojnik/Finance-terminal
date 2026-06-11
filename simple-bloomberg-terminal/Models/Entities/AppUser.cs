using Microsoft.AspNetCore.Identity;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// Application user. Extends <see cref="IdentityUser"/> (Id/UserName/Email/PasswordHash come from
/// Identity) with a single uploaded profile picture and its file metadata. The image bytes live on
/// disk under wwwroot/uploads/profiles; only the path + metadata are stored here.
/// </summary>
public class AppUser : IdentityUser
{
    // Web-relative path to the stored image, e.g. "/uploads/profiles/{userId}/{guid}.png".
    // Null = no picture (the placeholder is shown).
    public string? ProfilePicturePath { get; set; }

    // File metadata captured at upload time.
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}
