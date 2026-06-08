using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>Backs the phase-1 split-screen page (company picker + panes).</summary>
public class ExtractionIndexViewModel
{
    public long? CompanyId { get; set; }
    public string? CompanyLabel { get; set; }

    // Which graph node the page is building (REVENUE / COST / RISK). Drives the form fields, the
    // EDGAR-section filter and the AI prompts. Defaults to revenue (the original page).
    public ExtractionNode Node { get; set; } = ExtractionNode.REVENUE;

    // When the page is opened to add proof for one existing source row, these prefill the left
    // cells (and the JS binds the row) so the user browses/connects against its current values.
    public long? RevenueSourceId { get; set; }
    public SourceType? SourceType { get; set; }
    public string? Name { get; set; }
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public long? RelatedCompanyId { get; set; }
    public string? RelatedCompanyLabel { get; set; }
}

/// <summary>
/// One "Use as reference" action: the current state of the left cells (so the source row
/// can be created/updated) plus which cell is being proved and the selected proof text.
/// </summary>
public class ReferenceRequest
{
    public long CompanyId { get; set; }
    public long? RevenueSourceId { get; set; }   // null => create a new source row on save

    // Which node this row belongs to (REVENUE / COST / RISK), as the enum name. Decides the target
    // entity and the RelationKind stamped on the proof.
    public string Node { get; set; } = "REVENUE";

    // Left-cell values (written back to the source row, source of truth for the numbers).
    // Enums arrive as their string names from the browser (System.Text.Json web defaults bind
    // enums as numbers, so the controller parses these by name) — see ExtractionController.
    // SourceType is the generic classification string: SourceType / CostBase / RiskScope per node.
    public string SourceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public string? Note { get; set; }            // RISK node only (free-text)
    public long? RelatedCompanyId { get; set; }

    // The proof.
    public string Field { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;   // which EDGAR response backed it
    public string ReferencePointer { get; set; } = string.Empty;
    public string ReferenceSnapshot { get; set; } = string.Empty;
    public string? ReferencedValue { get; set; }

    // The filing the proof came from (sent only when a filing document is open in the right
    // pane — null when the proof was taken from Company Facts). Used to upsert a Filing and link
    // it to the source row, so the source connects to its proof filing on the graph.
    public string? FilingAccessionNumber { get; set; }
    public string? FilingForm { get; set; }
    public string? FilingDate { get; set; }
    public string? FilingUrl { get; set; }
}

/// <summary>Returned to the page so it can bind a freshly-created row id and flag the cell.</summary>
public record ReferenceResult(long RevenueSourceId, long ReviewId, string Field);

/// <summary>
/// One AI-extracted revenue source proposed from a filing (Mode B: AI fills, human reviews). The
/// page drops these values into the left cells and stashes each <see cref="Proof"/> snapshot so the
/// existing "Use as reference" save path can freeze it — no new write path. Nothing is persisted
/// until the human confirms a cell.
/// </summary>
public record ExtractionSuggestion(
    string Name, string? Classification, double? Value, double? Percentage,
    string? RelatedCompany, string Section, ExtractionProof Proof, string? Note = null);

/// <summary>Per-field verbatim proof text the model lifted from the chunk it read.</summary>
public record ExtractionProof(
    string? Name, string? Value, string? Percentage, string? Classification, string? RelatedCompany,
    string? Note = null);

/// <summary>Outcome of an auto-scan: how many headings the triage model chose to read in full, how
/// many candidate rows the workers pulled from them, and every heading it was offered with whether it
/// was picked — so the page can show the user what was triaged and what the AI selected.</summary>
public record AutoScanResult(int Scanned, int Found, IReadOnlyList<ScannedHeading> Headings);

/// <summary>One heading the triage model saw, plus whether it chose to scan it.</summary>
public record ScannedHeading(string Section, string Title, bool Picked);

/// <summary>
/// One "Save" of the whole left form: the source-row values plus every field that carries proof.
/// The controller upserts the <c>RevenueSource</c> once, then upserts a <c>SourceFieldReview</c>
/// per entry in <see cref="Proofs"/> — replacing the old one-button-per-cell flow.
/// </summary>
public class SaveRequest
{
    public long CompanyId { get; set; }
    public long? RevenueSourceId { get; set; }   // bound row id for the active node; null => new row
    public string Node { get; set; } = "REVENUE";
    public string SourceType { get; set; } = string.Empty;   // classification per node (Source/Cost/Scope)
    public string Name { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public string? Note { get; set; }            // RISK node only (free-text)
    public long? RelatedCompanyId { get; set; }

    public List<ProofInput> Proofs { get; set; } = [];
}

/// <summary>Proof for one field, carried inside a <see cref="SaveRequest"/>.</summary>
public class ProofInput
{
    public string Field { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ReferencePointer { get; set; } = string.Empty;
    public string ReferenceSnapshot { get; set; } = string.Empty;
    public string? ReferencedValue { get; set; }
    public string? FilingAccessionNumber { get; set; }
    public string? FilingForm { get; set; }
    public string? FilingDate { get; set; }
    public string? FilingUrl { get; set; }
}

/// <summary>
/// One counterparty the web-search model (Perplexity sonar) proposed for a specific business
/// <see cref="Segment"/> of a company: a named supplier or customer, where it would attach
/// (CUSTOMER => revenue source, SUPPLIER => cost source), the per-node classification, a one-line
/// note and a citation URL. Nothing is persisted until the user confirms via link-counterparty.
/// <see cref="ExistingCompanyId"/> is set when the name already matches a <c>Company</c> row, so the
/// page can show "link" vs "create + link".
/// </summary>
public record CounterpartySuggestion(
    string Name, string Side, string Segment, string Classification, string? Note, string? SourceUrl,
    string? CountryCode, string? Sector, string? Ticker, long? ExistingCompanyId,
    // Estimated USD value of the relationship/contract — only populated in "valued" discovery mode
    // (the BIGGEST-counterparties button); null otherwise.
    double? ContractValue = null);

/// <summary>
/// One event in a streamed discovery run (NDJSON to the page, like the chat). The planner first emits a
/// <c>plan</c> carrying the sub-queries it decomposed the company+segments into; then per sub-query a
/// <c>searching</c> (the query started) followed by a <c>result</c> (the named counterparties that
/// query surfaced). Lets the page render a live feed instead of waiting for one big answer.
/// </summary>
/// <param name="Type">plan | searching | result.</param>
/// <param name="Sources">On a <c>result</c>: the web pages that search fetched (citation URLs), so the
/// page can show a live "what's been fetched" list as each query lands.</param>
/// <param name="Error">On a <c>result</c>: set when that query's search FAILED (rather than genuinely
/// finding nothing), so the row can show why instead of a misleading "0 found".</param>
public record DiscoveryEvent(
    string Type,
    string? Query = null,
    IReadOnlyList<string>? Queries = null,
    IReadOnlyList<CounterpartySuggestion>? Items = null,
    IReadOnlyList<string>? Sources = null,
    string? Error = null);

/// <summary>
/// A segment-aware discovery run: find the named counterparties for each of the company's revenue
/// (Side=CUSTOMER) or cost (Side=SUPPLIER) <see cref="Segments"/>. The page sends the segment names it
/// already rendered, so the controller needn't re-query them.
/// </summary>
public class DiscoverCounterpartiesRequest
{
    public long CompanyId { get; set; }
    public string Side { get; set; } = "CUSTOMER";   // CUSTOMER => revenue segments, SUPPLIER => cost
    public List<string> Segments { get; set; } = [];
    // false (default) => the original mode: find the named counterparties per segment. true => the
    // "biggest counterparties + contract value" mode behind the second button (asks sonar for the
    // largest customers/suppliers AND the dollar value of each relationship).
    public bool Valued { get; set; }
}

/// <summary>
/// One confirmed counterparty link: resolve (or create) the counterparty <c>Company</c>, then create
/// a revenue source (CUSTOMER) or cost source (SUPPLIER) on the inspected company pointing at it via
/// RelatedCompanyId — feeding the graph's "RELATED COMPANIES" hub. CountryCode/Sector seed a brand-new
/// counterparty company (the <c>Company</c> ctor requires both); the controller falls back to the
/// inspecting company's country/sector when they don't resolve.
/// </summary>
public class LinkCounterpartyRequest
{
    public long CompanyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Side { get; set; } = "CUSTOMER";          // CUSTOMER => revenue, SUPPLIER => cost
    public string Classification { get; set; } = string.Empty; // SourceType (rev) or CostBase (cost) name
    public long? ExistingCompanyId { get; set; }
    public string? CountryCode { get; set; }
    public string? Sector { get; set; }
    public string? Ticker { get; set; }            // when set, a new counterparty is fetched from FMP
    public string? SourceUrl { get; set; }         // sonar citation — saved as proof on the linked row
    public string? Note { get; set; }              // sonar's one-line note, used as the proof snapshot
    public double? Value { get; set; }             // estimated contract value (USD) from valued discovery; stored on the row
}

/// <summary>One visible chat turn (the grounding/system context is added server-side, not here).</summary>
public record ChatMessage(string Role, string Content);

/// <summary>A chat send: which filing grounds the conversation + the visible turns so far.</summary>
public class ChatRequest
{
    public long CompanyId { get; set; }
    public string Accession { get; set; } = string.Empty;
    public string Doc { get; set; } = string.Empty;
    public string Node { get; set; } = "REVENUE";
    public string? Form { get; set; }   // SEC form (e.g. 10-K), passed to the sec2md sidecar
    public List<ChatMessage> Messages { get; set; } = [];
}
