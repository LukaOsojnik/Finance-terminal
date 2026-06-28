using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// Parsing-LLM transport for Anthropic's Messages API — the one provider that is NOT OpenAI-compatible.
/// Differences absorbed here: auth is the <c>x-api-key</c> header (not Bearer) plus a required
/// <c>anthropic-version</c>; the system prompt is a top-level field, not a message; <c>max_tokens</c> is
/// mandatory; and the reply is a <c>content[]</c> array of typed blocks rather than choices→message.
/// Base URL (https://api.anthropic.com) is wired in DI; the user's Anthropic key is per request.
/// </summary>
public sealed class AnthropicChatProvider : IChatProvider
{
    private const string Version = "2023-06-01";
    private const int DefaultMaxTokens = 8192;   // Anthropic requires a cap; chat passes none, so pick one.

    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;
    private readonly ILogger<AnthropicChatProvider> _logger;

    public ChatProviderId Id => ChatProviderId.Anthropic;

    public AnthropicChatProvider(HttpClient http, IUserApiKeyProvider keys, ILogger<AnthropicChatProvider> logger)
    {
        _http = http;
        _keys = keys;
        _logger = logger;
    }

    private Task<string> KeyAsync(CancellationToken ct) =>
        _keys.RequireAsync(k => k.Anthropic, MissingApiKeyException.Anthropic, ct);

    public async Task<string> CompleteAsync(
        string model, string system, string userPrompt,
        int maxTokens, bool jsonObject, CancellationToken ct)
    {
        // jsonObject is intentionally unused: Anthropic has no response_format toggle, so JSON-only
        // replies are driven by the system prompt (which every caller already phrases that way).
        var body = new
        {
            model,
            max_tokens = maxTokens,
            system,
            messages = new[] { new { role = "user", content = userPrompt } }
        };
        using var doc = await SendAsync(body, ct);
        return TextOf(doc.RootElement);
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string model, IReadOnlyList<DeepSeekMessage> messages,
        int? maxTokens, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Messages API forbids a "system" role in the array — fold any system turns into the top-level
        // system field, and pass only user/assistant turns as messages.
        var system = string.Join("\n\n", messages
            .Where(m => string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => m.Content));
        var turns = messages
            .Where(m => !string.Equals(m.Role, "system", StringComparison.OrdinalIgnoreCase))
            .Select(m => new { role = m.Role, content = m.Content });

        var body = new
        {
            model,
            max_tokens = maxTokens ?? DefaultMaxTokens,
            system,
            stream = true,
            messages = turns
        };

        using var httpReq = Request(body, await KeyAsync(ct));
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("Anthropic stream {Model} failed: {Status}", model, (int)resp.StatusCode);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;

            // Anthropic streams typed SSE events; we only care about content_block_delta, whose delta is
            // either a text_delta (the answer) or a thinking_delta (the reasoning trace, shown dim).
            JsonElement delta;
            try
            {
                using var d = JsonDocument.Parse(data);
                if (!d.RootElement.TryGetProperty("delta", out delta)) continue;
                delta = delta.Clone();
            }
            catch (JsonException ex) { _logger.LogDebug(ex, "Anthropic SSE parse error on line: {Data}", data); continue; }

            if (!delta.TryGetProperty("type", out var t) || t.GetString() is not { } kind) continue;
            if (kind == "thinking_delta" && delta.TryGetProperty("thinking", out var th) &&
                th.GetString() is { Length: > 0 } reasoning)
                yield return new ChatDelta("reasoning", reasoning);
            else if (kind == "text_delta" && delta.TryGetProperty("text", out var tx) &&
                tx.GetString() is { Length: > 0 } txt)
                yield return new ChatDelta("text", txt);
        }
    }

    private async Task<JsonDocument> SendAsync(object body, CancellationToken ct)
    {
        using var httpReq = Request(body, await KeyAsync(ct));
        var resp = await _http.SendAsync(httpReq, ct);
        if (!resp.IsSuccessStatusCode)
            _logger.LogWarning("Anthropic complete failed: {Status}", (int)resp.StatusCode);
        resp.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
    }

    private static HttpRequestMessage Request(object body, string key)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/v1/messages") { Content = JsonContent.Create(body) };
        req.Headers.Add("x-api-key", key);
        req.Headers.Add("anthropic-version", Version);
        return req;
    }

    // Concatenate the text blocks of a Messages response (skips thinking/other block types).
    private static string TextOf(JsonElement root)
    {
        if (!root.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            return "";
        var sb = new StringBuilder();
        foreach (var block in content.EnumerateArray())
            if (block.TryGetProperty("type", out var t) && t.GetString() == "text" &&
                block.TryGetProperty("text", out var txt) && txt.GetString() is { } s)
                sb.Append(s);
        return sb.ToString();
    }
}
