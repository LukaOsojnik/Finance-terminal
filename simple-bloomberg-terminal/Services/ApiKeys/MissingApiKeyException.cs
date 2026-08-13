namespace simple_bloomberg_terminal.Services.ApiKeys;

/// <summary>
/// Thrown by a keyed client when the current user hasn't provided their own API key for that
/// service. Carries the provider id (machine) and a human message. The
/// <c>MissingApiKeyExceptionFilter</c> turns it into an HTTP 424 + JSON for AJAX callers; full-page
/// form actions catch it and surface the message as a ModelState error instead.
/// </summary>
public class MissingApiKeyException : Exception
{
    public string Provider { get; }

    private MissingApiKeyException(string provider, string message) : base(message) => Provider = provider;

    // Factories keep the user-facing wording in one place (mirrored by site.js / the keys page).
    public static MissingApiKeyException DeepSeek() =>
        new("DeepSeek", "Parsing & structuring LLM API key missing");

    public static MissingApiKeyException Fmp() =>
        new("FMP", "Financial data (FMP key) missing");

    public static MissingApiKeyException Perplexity() =>
        new("Perplexity", "Web search (Perplexity key) missing");

    public static MissingApiKeyException Kimi() =>
        new("Kimi", "Kimi (Moonshot) API key missing");

    public static MissingApiKeyException OpenAi() =>
        new("OpenAI", "OpenAI API key missing");

    public static MissingApiKeyException Anthropic() =>
        new("Anthropic", "Anthropic API key missing");

    /// <summary>
    /// The signal for "the parsing provider this user actually routes to has no key". Callers gating
    /// an LLM action know the chosen <see cref="ChatProviderId"/> (from <c>UserApiKeys.ParsingProvider</c>)
    /// but not which factory above matches it.
    /// </summary>
    /// <remarks>Unknown ids throw rather than falling back to a default provider: sending the user to
    /// the wrong "add your key" popup is a worse failure than a loud one, and it matches
    /// <see cref="Llm.ChatProviders.Info"/>, which also refuses an id it doesn't know.</remarks>
    public static MissingApiKeyException ForParsingProvider(ChatProviderId id) => id switch
    {
        ChatProviderId.DeepSeek => DeepSeek(),
        ChatProviderId.Kimi => Kimi(),
        ChatProviderId.OpenAi => OpenAi(),
        ChatProviderId.Anthropic => Anthropic(),
        _ => throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown parsing provider."),
    };
}
