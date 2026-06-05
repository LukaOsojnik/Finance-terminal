namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// Which graph node the extraction page is currently building. Drives the SEC Items scanned, the
/// AI prompts, the left-form fields, and which entity a save lands in.
/// </summary>
public enum ExtractionNode
{
    REVENUE,
    COST,
    RISK
}
