namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>
/// Read-only status of the signed-in user's bring-your-own API keys for the keys page. Never carries
/// a raw key back to the browser — only whether each is set and its last 4 characters for recognition.
/// </summary>
public class ApiKeysViewModel
{
    public ApiKeyStatus DeepSeek { get; set; } = new();
    public ApiKeyStatus Fmp { get; set; } = new();
    public ApiKeyStatus Perplexity { get; set; } = new();
}

public class ApiKeyStatus
{
    public bool IsSet { get; set; }
    public string? Last4 { get; set; }
}
