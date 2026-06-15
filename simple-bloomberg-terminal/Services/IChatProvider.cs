namespace simple_bloomberg_terminal.Services;

/// <summary>
/// One parsing-LLM provider transport. Same two operations the chat role needs (a one-shot JSON/text
/// completion and a streaming multi-turn completion), but each implementation owns its own wire shape,
/// auth header, parameter quirks, and key lookup. The router picks one of these by the user's choice.
/// </summary>
public interface IChatProvider
{
    ChatProviderId Id { get; }

    Task<string> CompleteAsync(
        string model, string system, string userPrompt,
        int maxTokens, bool jsonObject, CancellationToken ct);

    IAsyncEnumerable<ChatDelta> StreamAsync(
        string model, IReadOnlyList<DeepSeekMessage> messages,
        int? maxTokens, CancellationToken ct);
}
