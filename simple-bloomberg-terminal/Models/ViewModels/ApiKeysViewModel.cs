
namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>
/// Read-only status of the signed-in user's bring-your-own API keys for the keys page, plus the
/// per-user model-routing choices. Never carries a raw key back to the browser â€” only whether each is
/// set and its last 4 characters for recognition.
/// </summary>
public class ApiKeysViewModel
{
    public ApiKeyStatus DeepSeek { get; set; } = new();
    public ApiKeyStatus Fmp { get; set; } = new();
    public ApiKeyStatus Perplexity { get; set; } = new();
    public ApiKeyStatus Kimi { get; set; } = new();
    public ApiKeyStatus OpenAi { get; set; } = new();
    public ApiKeyStatus Anthropic { get; set; } = new();

    // Current routing selections (which provider/model runs each role).
    public ChatProviderId ParsingProvider { get; set; } = ChatProviderId.DeepSeek;
    public string ParsingModel { get; set; } = "";
    public string WebSearchModel { get; set; } = ChatProviders.DefaultWebSearchModel;
}

public class ApiKeyStatus
{
    public bool IsSet { get; set; }
    public string? Last4 { get; set; }
}
