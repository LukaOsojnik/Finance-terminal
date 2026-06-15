using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <inheritdoc cref="IUserApiKeyProvider"/>
public class UserApiKeyProvider : IUserApiKeyProvider
{
    // Purpose string scopes the protector — ciphertext from one purpose can't be unprotected by
    // another. Bump the suffix only if the format ever changes.
    public const string Purpose = "UserApiKeys.v1";

    private readonly IHttpContextAccessor _http;
    private readonly IUserApiKeyRepository _repo;
    private readonly IDataProtector _protector;
    private UserApiKeys? _cached;

    public UserApiKeyProvider(IHttpContextAccessor http, IUserApiKeyRepository repo, IDataProtectionProvider dp)
    {
        _http = http;
        _repo = repo;
        _protector = dp.CreateProtector(Purpose);
    }

    public void Set(UserApiKeys keys) => _cached = keys;

    public async Task<UserApiKeys> GetAsync(CancellationToken ct = default)
    {
        if (_cached is not null) return _cached;

        var userId = CurrentUserId();
        if (string.IsNullOrEmpty(userId)) return _cached = UserApiKeys.Empty;

        var row = await _repo.GetAsync(userId, ct);
        if (row is null) return _cached = UserApiKeys.Empty;

        return _cached = new UserApiKeys(
            Decrypt(row.DeepSeekKey),
            Decrypt(row.FmpKey),
            Decrypt(row.PerplexityKey));
    }

    public async Task SaveAsync(ApiKeyEdit edit, CancellationToken ct = default)
    {
        var userId = CurrentUserId();
        if (string.IsNullOrEmpty(userId)) return;

        var row = await _repo.GetAsync(userId, ct) ?? new UserApiKey { UserId = userId };

        row.DeepSeekKey = Apply(row.DeepSeekKey, edit.DeepSeek, edit.ClearDeepSeek);
        row.FmpKey = Apply(row.FmpKey, edit.Fmp, edit.ClearFmp);
        row.PerplexityKey = Apply(row.PerplexityKey, edit.Perplexity, edit.ClearPerplexity);

        await _repo.UpsertAsync(row, ct);
        _cached = null; // next GetAsync re-reads the saved values
    }

    private string? CurrentUserId() => _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);

    // Decide a column's new ciphertext: clear -> null; new value -> encrypt; blank -> keep existing.
    private string? Apply(string? current, string? input, bool clear)
    {
        if (clear) return null;
        if (string.IsNullOrWhiteSpace(input)) return current;
        return _protector.Protect(input.Trim());
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
