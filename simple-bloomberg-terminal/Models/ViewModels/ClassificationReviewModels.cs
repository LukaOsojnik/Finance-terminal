using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>
/// One row on the classification-review page: the company alongside its raw FMP source label and the
/// resolved Sector/Industry/Sub-industry, so a human can spot a mis-fit (label-beside-result) that the
/// pipeline marked "Resolved" and would otherwise never surface.
/// </summary>
public record ClassificationRow(
    long Id,
    string Name,
    string? Label,
    string Sector,
    string Industry,
    string SubIndustry,
    ClassifyStatus Status,
    bool Locked,
    // True when this company's FMP label resolved to MORE THAN ONE distinct sub-industry across the book
    // — i.e. the label is ambiguous/colliding (e.g. two firms both labelled "REIT - Industrial" landing in
    // different subs). A strong "look here first" signal for mis-fits the cache can't disambiguate.
    bool Flagged,
    bool Resolved);
