using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Typed HttpClient for the DeepSeek chat-completions API (OpenAI-compatible). Base URL comes from
/// the "DeepSeek" config section; the Bearer key is the CURRENT USER's own key (bring-your-own),
/// resolved per request from <see cref="IUserApiKeyProvider"/> — no global key. A user without a
/// DeepSeek key triggers a <see cref="MissingApiKeyException"/>. The auth header is set per
/// HttpRequestMessage (not on the shared client) so concurrent calls in one scope can't clobber it.
/// </summary>
public class DeepSeekClient : IDeepSeekClient
{
    private readonly HttpClient _http;
    private readonly IUserApiKeyProvider _keys;

    public DeepSeekClient(HttpClient http, IConfiguration config, IUserApiKeyProvider keys)
    {
        _http = http;
        _keys = keys;
        var section = config.GetSection("DeepSeek");
        _http.BaseAddress = new Uri(section["BaseUrl"] ?? "https://api.deepseek.com");
        _http.Timeout = TimeSpan.FromMinutes(2);   // filing chunks can be large
    }

    // The user's DeepSeek key, or throw the "add your key" signal the front-end turns into a popup.
    private async Task<string> KeyAsync(CancellationToken ct)
    {
        var k = (await _keys.GetAsync(ct)).DeepSeek;
        if (string.IsNullOrWhiteSpace(k)) throw MissingApiKeyException.DeepSeek();
        return k;
    }

    public async Task<string> CompleteAsync(
        string model, string system, string userPrompt,
        int maxTokens = 4096, bool jsonObject = false, CancellationToken ct = default)
    {
        var req = new DeepSeekRequest(
            Model: model,
            Messages:
            [
                new DeepSeekMessage("system", system),
                new DeepSeekMessage("user", userPrompt)
            ],
            MaxTokens: maxTokens,
            ResponseFormat: jsonObject ? new DeepSeekResponseFormat("json_object") : null);

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(req),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<DeepSeekResponse>(ct);

        return body?.Choices?.FirstOrDefault()?.Message?.Content ?? "";
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        string model, IReadOnlyList<DeepSeekMessage> messages,
        int? maxTokens = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        var req = new DeepSeekRequest(model, messages.ToList(), maxTokens, Stream: true);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(req),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        // ResponseHeadersRead = don't buffer the whole body; hand us the stream as it arrives.
        using var resp = await _http.SendAsync(httpReq, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line["data:".Length..].Trim();
            if (data.Length == 0) continue;
            if (data == "[DONE]") break;

            // One SSE frame: choices[0].delta carries either reasoning_content or content (or neither
            // on the first role-only frame). Yield each as a typed fragment.
            JsonElement delta;
            try
            {
                using var doc = JsonDocument.Parse(data);
                delta = doc.RootElement.GetProperty("choices")[0].GetProperty("delta").Clone();
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or IndexOutOfRangeException)
            {
                continue;
            }

            if (delta.TryGetProperty("reasoning_content", out var r) &&
                r.ValueKind == JsonValueKind.String && r.GetString() is { Length: > 0 } rt)
                yield return new ChatDelta("reasoning", rt);

            if (delta.TryGetProperty("content", out var c) &&
                c.ValueKind == JsonValueKind.String && c.GetString() is { Length: > 0 } ct2)
                yield return new ChatDelta("text", ct2);
        }
    }
}
