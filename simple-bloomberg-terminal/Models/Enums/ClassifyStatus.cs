namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// Where a company sits in the GICS sub-industry classification pipeline. Lets the "Unclassified"
/// report distinguish a company that was never attempted from one the LLM genuinely couldn't place.
/// </summary>
public enum ClassifyStatus
{
    /// <summary>Not yet classified (or awaiting a backfill). The default for a brand-new row.</summary>
    Pending,

    /// <summary>A GICS sub-industry was assigned (Industry/Sector roll up from it).</summary>
    Resolved,

    /// <summary>The classifier ran — constrained then unconstrained — and still found no fitting
    /// sub-industry. A genuine taxonomy gap or a junk row; surfaced for manual / AI re-resolution.</summary>
    NoFit
}
