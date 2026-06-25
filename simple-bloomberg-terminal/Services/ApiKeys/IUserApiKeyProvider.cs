namespace simple_bloomberg_terminal.Services.ApiKeys;

/// <summary>
/// Decrypted API keys for one user (null = not provided) plus the user's model-routing choices. The
/// first three params are positional-stable for callers/tests that predate the multi-provider work;
/// everything after has a default so <c>new UserApiKeys("ds", null, null)</c> still compiles.
/// </summary>
public record UserApiKeys(
    string? DeepSeek, string? Fmp, string? Perplexity,
    string? Kimi = null, string? OpenAi = null, string? Anthropic = null,
    ChatProviderId ParsingProvider = ChatProviderId.DeepSeek,
    string? ParsingModel = null, string? WebSearchModel = null)
{
    public static readonly UserApiKeys Empty = new(null, null, null);

    /// <summary>The parsing key for a given provider (the one the router needs to call it).</summary>
    public string? KeyFor(ChatProviderId id) => id switch
    {
        ChatProviderId.DeepSeek => DeepSeek,
        ChatProviderId.Kimi => Kimi,
        ChatProviderId.OpenAi => OpenAi,
        ChatProviderId.Anthropic => Anthropic,
        _ => null
    };
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

    // Persist the current user's keys from a profile-page submission (encrypts new values, honours
    // per-key clear/keep). Owns the encryption so the controller never touches the protector.
    Task SaveAsync(ApiKeyEdit edit, CancellationToken ct = default);
}

/// <summary>
/// One API-keys page submission. Per key: a new value (blank = keep existing) or a clear flag. Plus
/// the model-routing choices, which are always overwritten with whatever the form posted.
/// </summary>
public record ApiKeyEdit(
    string? DeepSeek, bool ClearDeepSeek,
    string? Fmp, bool ClearFmp,
    string? Perplexity, bool ClearPerplexity,
    string? Kimi = null, bool ClearKimi = false,
    string? OpenAi = null, bool ClearOpenAi = false,
    string? Anthropic = null, bool ClearAnthropic = false,
    ChatProviderId ParsingProvider = ChatProviderId.DeepSeek,
    string? ParsingModel = null, string? WebSearchModel = null);

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
