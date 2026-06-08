namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP-only boundary to the DeepSeek chat-completions API (OpenAI-compatible). Send a system
/// prompt + one user message, return the model's text. <paramref name="jsonObject"/> asks DeepSeek
/// to guarantee a valid-JSON reply (the prompt must mention JSON). Registered as a typed
/// <c>HttpClient</c>. Transport failure throws; callers map that to 503.
/// </summary>
public interface IDeepSeekClient
{
    Task<string> CompleteAsync(
        string model, string system, string userPrompt,
        int maxTokens = 4096, bool jsonObject = false, CancellationToken ct = default);

    /// <summary>
    /// Streaming multi-turn completion. Yields <see cref="ChatDelta"/> fragments as they arrive —
    /// "reasoning" (v4 thinking trace) and "text" (answer) — so the UI can render them live.
    /// <paramref name="maxTokens"/> null (the default) sends no cap, letting the model run to its own limit.
    /// </summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(
        string model, IReadOnlyList<DeepSeekMessage> messages,
        int? maxTokens = null, CancellationToken ct = default);
}
