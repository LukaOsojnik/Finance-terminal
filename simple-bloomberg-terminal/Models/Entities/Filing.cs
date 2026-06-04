using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A SEC EDGAR filing used as proof for a cost/revenue source. Identity is the EDGAR
/// <see cref="AccessionNumber"/> (globally unique), so the same filing is stored once and
/// linked from every source row it backs. Distinct from <see cref="Event"/>, which carries
/// the same filings as market-timeline items (no accession, no source link).
/// </summary>
public class Filing
{
    [Key]
    public long Id { get; set; }

    public long CompanyId { get; set; }
    public string AccessionNumber { get; set; } = string.Empty;
    public string? Form { get; set; }
    public DateTime? FilingDate { get; set; }
    public string? PrimaryDocUrl { get; set; }
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }
}
