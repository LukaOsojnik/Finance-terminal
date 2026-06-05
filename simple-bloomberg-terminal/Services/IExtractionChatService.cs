using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Conversational Mode B: a chat grounded on one open SEC filing. The user discusses the filing;
/// the model surfaces interesting revenue/cost sources and, when asked to save one, emits a fenced
/// <c>```save {json}```</c> block the page turns into a form pre-fill. Streams reasoning + answer
/// fragments live. Holds no conversation state — the page resends the visible turns each send;
/// the filing grounding is rebuilt server-side (cached) and never travels to the client.
/// </summary>
public interface IExtractionChatService
{
    IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history, CancellationToken ct = default);
}
