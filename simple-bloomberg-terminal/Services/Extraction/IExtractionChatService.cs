using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

/// <summary>
/// Conversational Mode B: a chat grounded on one open SEC filing. The user discusses the filing;
/// the model surfaces interesting revenue/cost sources and, when asked to save one, emits a fenced
/// <c>```save {json}```</c> block the page turns into a form pre-fill. Streams reasoning + answer
/// fragments live. Holds no conversation state â€” the page resends the visible turns each send;
/// the filing grounding is rebuilt server-side (cached) and never travels to the client.
/// </summary>
public interface IExtractionChatService
{
    // handoff=true marks this turn as a cross-segment hand-off being RECEIVED: ground only on cached
    // findings (no worker fan-out â€” the source agent already found the fact) AND use the receiver
    // system prompt (record it, don't re-route, null values OK). See docs/extraction/cross-extraction.md.
    IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history, string? filingType = null, bool handoff = false,
        CancellationToken ct = default);

    // The structured audited XBRL facts for one filing+node â€” the same figures the chat grounding text
    // is built from, surfaced for the UI. Numeric nodes only (COST, REVENUE); null for RISK or when the
    // filing tags nothing. Cached per filing (one SEC round-trip), so it's cheap to call from the widget.
    Task<XbrlView?> GetXbrlViewAsync(
        long companyId, string accession, ExtractionNode node, CancellationToken ct = default);
}
