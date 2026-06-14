namespace simple_bloomberg_terminal.Services;

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
}
