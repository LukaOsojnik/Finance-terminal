using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="IUserApiKeyProvider"/>
public class UserApiKeyProvider : IUserApiKeyProvider
{
    // Purpose string scopes the protector — ciphertext from one purpose can't be unprotected by
    // another. Bump the suffix only if the format ever changes.
    public const string Purpose = "UserApiKeys.v1";

    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;
    private readonly IDataProtector _protector;
    private UserApiKeys? _cached;

    public UserApiKeyProvider(IHttpContextAccessor http, AppDbContext db, IDataProtectionProvider dp)
    {
        _http = http;
        _db = db;
        _protector = dp.CreateProtector(Purpose);
    }

    public void Set(UserApiKeys keys) => _cached = keys;

    public async Task<UserApiKeys> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var userId = _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId)) return _cached = UserApiKeys.Empty;

        var row = await _db.UserApiKeys.AsNoTracking().FirstOrDefaultAsync(k => k.UserId == userId, ct);
        if (row is null) return _cached = UserApiKeys.Empty;

        return _cached = new UserApiKeys(
            Decrypt(row.DeepSeekKey),
            Decrypt(row.FmpKey),
            Decrypt(row.PerplexityKey));
    }

    // Ciphertext -> plaintext. A protector/key-ring change (or tampered value) throws
    // CryptographicException — treat that as "no usable key" rather than crashing the request.
    private string? Decrypt(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return null;
        try { return _protector.Unprotect(cipher); }
        catch (CryptographicException) { return null; }
    }
}
