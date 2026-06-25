using System.Runtime.CompilerServices;

namespace simple_bloomberg_terminal.Services.Llm;

/// <summary>
/// The parsing &amp; structuring LLM, as the rest of the app sees it. Callers no longer pass a model or
/// know a provider — they hand over a prompt and get text/stream back. Which provider+model actually
/// runs is the signed-in user's stored choice, resolved per request. Replaces the direct
/// <c>IDeepSeekClient</c> dependency in the extraction/chat/classifier/reviewer services.
/// </summary>
public interface IChatLlm
{
    /// <param name="fast">Route to the provider's fast/cheap model instead of the user's chosen default
    /// tier — for high-volume parallel work (the filing scan's triage + per-chunk workers).</param>
    Task<string> CompleteAsync(
        string system, string userPrompt,
        int maxTokens = 4096, bool jsonObject = false, bool fast = false, CancellationToken ct = default);

    IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<DeepSeekMessage> messages, int? maxTokens = null, CancellationToken ct = default);

    /// <summary>The provider+model this request will use — for labelling stored output (e.g. ReviewerModel).</summary>
    Task<(ChatProviderId Provider, string Model)> ResolveParsingAsync(CancellationToken ct = default);
}

/// <inheritdoc cref="IChatLlm"/>
public sealed class ChatLlmRouter : IChatLlm
{
    private readonly IUserApiKeyProvider _keys;
    private readonly IReadOnlyDictionary<ChatProviderId, IChatProvider> _providers;

    public ChatLlmRouter(IUserApiKeyProvider keys, IEnumerable<IChatProvider> providers)
    {
        _keys = keys;
        // Last registration wins per id; in practice each provider id is registered once.
        _providers = providers.ToDictionary(p => p.Id);
    }

    public async Task<(ChatProviderId Provider, string Model)> ResolveParsingAsync(CancellationToken ct = default)
    {
        var keys = await _keys.GetAsync(ct);
        return (keys.ParsingProvider, ChatProviders.ResolveModel(keys.ParsingProvider, keys.ParsingModel));
    }

    public async Task<string> CompleteAsync(
        string system, string userPrompt,
        int maxTokens = 4096, bool jsonObject = false, bool fast = false, CancellationToken ct = default)
    {
        var keys = await _keys.GetAsync(ct);
        // Same provider (so one API key serves both), but the fast tier for parallel scan work.
        var model = fast
            ? ChatProviders.FastModel(keys.ParsingProvider)
            : ChatProviders.ResolveModel(keys.ParsingProvider, keys.ParsingModel);
        return await Provider(keys.ParsingProvider).CompleteAsync(model, system, userPrompt, maxTokens, jsonObject, ct);
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        IReadOnlyList<DeepSeekMessage> messages, int? maxTokens = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var (id, model) = await ResolveParsingAsync(ct);
        await foreach (var d in Provider(id).StreamAsync(model, messages, maxTokens, ct))
            yield return d;
    }

    // The chosen provider, or a clear failure if it somehow wasn't registered.
    private IChatProvider Provider(ChatProviderId id) =>
        _providers.TryGetValue(id, out var p) ? p
            : throw new InvalidOperationException($"No chat provider registered for {id}.");
}
