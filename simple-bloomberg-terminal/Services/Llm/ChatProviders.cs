namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// The chat/parsing LLM providers the app can route the "parsing &amp; structuring" role to. The
/// web-search role is always Perplexity (its sonar models do search + answer in one call, so it
/// can't be swapped for a plain chat provider) and is therefore not in this enum.
/// </summary>
public enum ChatProviderId
{
    DeepSeek,
    Kimi,
    OpenAi,
    Anthropic
}

/// <summary>
/// Static catalog of the selectable providers/models. Single source of truth shared by the API-keys
/// page (renders the dropdowns), the persistence layer (validates a stored choice), and the router
/// (default model when the user hasn't picked one). Add a model here and it appears everywhere.
/// </summary>
public static class ChatProviders
{
    /// <summary>One parsing provider: its display name, the models the dropdown offers (most capable
    /// first), the default when none is chosen, the fast/cheap model for high-volume parallel work,
    /// and where to get a key.</summary>
    public record ProviderInfo(
        ChatProviderId Id, string Display, string KeyLabel, string KeyHelpUrl,
        IReadOnlyList<string> Models, string DefaultModel, string FastModel);

    // Models current as of 2026-06. Most capable / default first; FastModel is the cheap/quick tier
    // used for the parallel filing scan (triage + per-chunk workers).
    public static readonly IReadOnlyList<ProviderInfo> Parsing =
    [
        new(ChatProviderId.DeepSeek, "DeepSeek", "DeepSeek", "https://platform.deepseek.com/api_keys",
            ["deepseek-v4-pro", "deepseek-v4-flash"], "deepseek-v4-pro", "deepseek-v4-flash"),
        new(ChatProviderId.Kimi, "Kimi (Moonshot)", "Kimi", "https://platform.moonshot.ai/console/api-keys",
            ["kimi-k2.6", "kimi-k2.5"], "kimi-k2.6", "kimi-k2.5"),
        new(ChatProviderId.OpenAi, "OpenAI", "OpenAI", "https://platform.openai.com/api-keys",
            ["gpt-5.5", "gpt-5", "gpt-5-mini"], "gpt-5", "gpt-5-mini"),
        new(ChatProviderId.Anthropic, "Anthropic", "Anthropic", "https://console.anthropic.com/settings/keys",
            ["claude-opus-4-8", "claude-sonnet-4-6", "claude-haiku-4-5"], "claude-sonnet-4-6", "claude-haiku-4-5"),
    ];

    /// <summary>Perplexity sonar variants for the web-search role (provider is always Perplexity).</summary>
    public static readonly IReadOnlyList<string> WebSearchModels =
        ["sonar-pro", "sonar", "sonar-reasoning-pro", "sonar-deep-research"];

    public const string DefaultWebSearchModel = "sonar-pro";

    public static ProviderInfo Info(ChatProviderId id) => Parsing.First(p => p.Id == id);

    /// <summary>The provider's fast/cheap model — used for the high-volume parallel filing scan (triage
    /// + per-chunk workers), where the heavyweight default is slow, costly, and (for reasoners) starves
    /// the small per-call token budget. The interactive chat keeps the user's chosen default tier.</summary>
    public static string FastModel(ChatProviderId id) => Info(id).FastModel;

    /// <summary>The chosen model, falling back to the provider default if it's blank or no longer offered.</summary>
    public static string ResolveModel(ChatProviderId id, string? chosen)
    {
        var info = Info(id);
        return !string.IsNullOrWhiteSpace(chosen) && info.Models.Contains(chosen) ? chosen : info.DefaultModel;
    }

    /// <summary>Parse a stored provider name back to the enum, defaulting to DeepSeek (the app's original).</summary>
    public static ChatProviderId ParseProvider(string? stored) =>
        Enum.TryParse<ChatProviderId>(stored, out var id) ? id : ChatProviderId.DeepSeek;
}
