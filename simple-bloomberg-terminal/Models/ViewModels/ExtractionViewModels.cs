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
