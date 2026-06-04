using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>Backs the phase-1 split-screen page (company picker + panes).</summary>
public class ExtractionIndexViewModel
{
    public long? CompanyId { get; set; }
    public string? CompanyLabel { get; set; }

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

    // Left-cell values (written back to the source row, source of truth for the numbers).
    // Enums arrive as their string names from the browser (System.Text.Json web defaults bind
    // enums as numbers, so the controller parses these by name) — see ExtractionController.
    public string SourceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Percentage { get; set; }
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
    string? RelatedCompany, string Section, ExtractionProof Proof);

/// <summary>Per-field verbatim proof text the model lifted from the chunk it read.</summary>
public record ExtractionProof(
    string? Name, string? Value, string? Percentage, string? Classification, string? RelatedCompany);

/// <summary>One selectable bold sub-heading shown in the "pick sections" list. <c>Id</c> indexes the
/// cached heading list so a later scan can map the user's ticks back to the paragraphs to read.</summary>
public record HeadingInfo(int Id, string Title, string Section, int Chars);

/// <summary>
/// One "Save" of the whole left form: the source-row values plus every field that carries proof.
/// The controller upserts the <c>RevenueSource</c> once, then upserts a <c>SourceFieldReview</c>
/// per entry in <see cref="Proofs"/> — replacing the old one-button-per-cell flow.
/// </summary>
public class SaveRequest
{
    public long CompanyId { get; set; }
    public long? RevenueSourceId { get; set; }   // null => create a new row
    public string SourceType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public double? Value { get; set; }
    public double? Percentage { get; set; }
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

/// <summary>One visible chat turn (the grounding/system context is added server-side, not here).</summary>
public record ChatMessage(string Role, string Content);

/// <summary>A chat send: which filing grounds the conversation + the visible turns so far.</summary>
public class ChatRequest
{
    public long CompanyId { get; set; }
    public string Accession { get; set; } = string.Empty;
    public string Doc { get; set; } = string.Empty;
    public List<ChatMessage> Messages { get; set; } = [];
}
