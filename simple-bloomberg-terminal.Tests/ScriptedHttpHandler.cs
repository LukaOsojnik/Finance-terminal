using System.Net;
using System.Text;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// A test <see cref="HttpMessageHandler"/> that answers from a supplied responder instead of the
/// network, so the real typed clients (<c>DeepSeekClient</c>, <c>CounterpartyDiscoveryService</c>)
/// run their actual request-shaping and response-parsing against canned LLM payloads. Captures every
/// request (body + Authorization header) for assertions; the capture list is locked because the
/// discovery service fires its sub-query searches in parallel.
/// </summary>
public sealed class ScriptedHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, string, HttpResponseMessage> _responder;
    private readonly object _lock = new();

    public List<CapturedRequest> Captured { get; } = [];

    public ScriptedHttpHandler(Func<HttpRequestMessage, string, HttpResponseMessage> responder) =>
        _responder = responder;

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
        var captured = new CapturedRequest(request.RequestUri, request.Headers.Authorization?.ToString(), body);
        lock (_lock) Captured.Add(captured);
        return _responder(request, body);
    }

    public CapturedRequest Single()
    {
        lock (_lock) return Captured.Single();
    }

    // Always answer 200 with the same JSON body (the request is ignored).
    public static ScriptedHttpHandler Json(string json) => new((_, _) => JsonResponse(json));

    // Always answer 200 with a Server-Sent-Events stream body (DeepSeek streaming completions).
    public static ScriptedHttpHandler Sse(string body) => new((_, _) =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/event-stream")
        });

    public static HttpResponseMessage JsonResponse(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
}

/// <summary>One captured outbound call: the request URI, its Bearer header, and the raw JSON body.</summary>
public sealed record CapturedRequest(Uri? Uri, string? Authorization, string Body);
