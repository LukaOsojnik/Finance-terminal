using Microsoft.AspNetCore.Identity;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// Application user. Extends <see cref="IdentityUser"/> (Id/UserName/Email/PasswordHash come from
/// Identity) with a single uploaded profile picture and its file metadata. The image bytes are
/// stored in the DB row so they survive container redeploys (ephemeral filesystems lose disk files);
/// they are served back by AccountController.Picture.
/// </summary>
public class AppUser : IdentityUser
{
    // Raw image bytes. Null = no picture (the placeholder is shown).
    public byte[]? ProfilePictureData { get; set; }

    // File metadata captured at upload time.
    public string? OriginalFileName { get; set; }
    public string? ContentType { get; set; }
    public long? SizeBytes { get; set; }
    public DateTime? UploadedAt { get; set; }
}
