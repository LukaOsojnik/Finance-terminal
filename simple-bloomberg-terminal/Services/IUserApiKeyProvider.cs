namespace simple_bloomberg_terminal.Services;

/// <summary>Decrypted API keys for one user. Null = not provided.</summary>
public record UserApiKeys(string? DeepSeek, string? Fmp, string? Perplexity)
{
    public static readonly UserApiKeys Empty = new(null, null, null);
}

/// <summary>
/// Resolves the current user's bring-your-own API keys (decrypted) for the keyed clients. Scoped:
/// one instance per request, the loaded keys cached for that request. Detached background jobs run
/// outside any HttpContext, so they pass the keys they captured at request time via <see cref="Set"/>
/// before resolving a keyed client in their own scope.
/// </summary>
public interface IUserApiKeyProvider
{
    Task<UserApiKeys> GetAsync(CancellationToken ct = default);

    // Override the keys for this scope (used by detached jobs that have no HttpContext).
    void Set(UserApiKeys keys);
}

public static class UserApiKeyProviderExtensions
{
    /// <summary>
    /// Resolve one required key, throwing the matching <see cref="MissingApiKeyException"/> when the
    /// user hasn't provided it. Centralizes the "blank => throw the add-your-key signal" rule the
    /// keyed clients all share.
    /// </summary>
    public static async Task<string> RequireAsync(
        this IUserApiKeyProvider keys,
        Func<UserApiKeys, string?> pick,
        Func<MissingApiKeyException> ifMissing,
        CancellationToken ct = default)
    {
        var k = pick(await keys.GetAsync(ct));
        if (string.IsNullOrWhiteSpace(k)) throw ifMissing();
        return k;
    }
}
