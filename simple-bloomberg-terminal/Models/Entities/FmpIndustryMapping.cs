using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// The learned vendor-label → GICS sub-industry cache. Each distinct FMP industry label is mapped
/// once (by the LLM, on first sighting) and reused thereafter, so the same label never costs a second
/// model call. Label is unique; the parent Industry/Sector are derived from the sub-industry, not stored.
/// </summary>
public class FmpIndustryMapping
{
    public FmpIndustryMapping(string label, GicsSubIndustry subIndustry)
    {
        Label = label;
        SubIndustry = subIndustry;
    }

    [Key]
    public long Id { get; set; }

    // The raw FMP profile.industry string, normalized to a stable lookup key (see the repository).
    [StringLength(160)]
    public string Label { get; set; }

    public GicsSubIndustry SubIndustry { get; set; }
}
