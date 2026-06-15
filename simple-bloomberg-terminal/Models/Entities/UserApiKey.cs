namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A user's own (bring-your-own) third-party API keys for the keyed external services. One row per
/// user (1:1 with <see cref="AppUser"/> via the shared PK <see cref="UserId"/>). Each column holds
/// the key as CIPHERTEXT — encrypted with the ASP.NET Data Protection API by
/// <c>UserApiKeyProvider</c>, never the raw key — so a DB dump leaks nothing usable. Null = the user
/// hasn't provided that key, and the feature that needs it surfaces a "missing key" prompt.
/// </summary>
public class UserApiKey
{
    // PK and FK to AppUser.Id at once (shared-primary-key 1:1).
    public string UserId { get; set; } = "";

    public string? DeepSeekKey { get; set; }
    public string? FmpKey { get; set; }
    public string? PerplexityKey { get; set; }

    // Additional parsing-LLM provider keys (ciphertext, same as above). Null = not provided.
    public string? KimiKey { get; set; }
    public string? OpenAiKey { get; set; }
    public string? AnthropicKey { get; set; }

    // Per-user model routing (NOT secrets — stored plaintext). Which provider/model handles the
    // parsing & structuring role, and which Perplexity sonar variant handles web search. Null = use
    // the app default (DeepSeek / sonar-pro), so existing rows keep working unchanged.
    public string? ParsingProvider { get; set; }
    public string? ParsingModel { get; set; }
    public string? WebSearchModel { get; set; }
}
