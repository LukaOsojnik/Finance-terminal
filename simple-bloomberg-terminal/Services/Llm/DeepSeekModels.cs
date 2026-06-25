using System.Text.Json.Serialization;

namespace simple_bloomberg_terminal.Services.Llm;

// Minimal request/response shapes for POST /chat/completions (OpenAI-compatible). Web JSON
// defaults are camelCase; the API wants snake_case, so differing keys carry an explicit attribute.

public record DeepSeekRequest(
    string Model,
    List<DeepSeekMessage> Messages,
    // Nullable so it can be omitted entirely (null → no max_tokens sent → the model uses its own
    // output ceiling, not a fixed cap that can truncate the reply mid-sentence).
    [property: JsonPropertyName("max_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTokens,
    bool Stream = false,
    [property: JsonPropertyName("response_format")] DeepSeekResponseFormat? ResponseFormat = null);

public record DeepSeekMessage(string Role, string Content);

public record DeepSeekResponseFormat(string Type);

// Response: { "choices": [ { "message": { "role": "assistant", "content": "..." } } ] }
public record DeepSeekResponse(List<DeepSeekChoice>? Choices);

public record DeepSeekChoice(DeepSeekMessage? Message);

// One streamed fragment surfaced to the chat. Kind is "reasoning" (v4 thinking trace, shown dim)
// or "text" (the actual answer the user keeps).
public record ChatDelta(string Kind, string Text);
