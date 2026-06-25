using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

/// <summary>
/// The learned FMP-label → GICS sub-industry cache. Lookups and writes both key off a normalized form
/// of the label so casing / punctuation variants of the same vendor label collapse to one row.
/// </summary>
public interface IFmpIndustryMappingRepository
{
    // Cached sub-industry for this vendor label, or null if the label was never mapped.
    GicsSubIndustry? Get(string? label);

    // Persist (or refresh) the mapping for a label. No-op for a blank label.
    void Set(string? label, GicsSubIndustry subIndustry);

    // Forget a label's learned mapping (no-op if absent). Called when a human corrects a company whose
    // label produced a wrong/ambiguous cached sub-industry, so the poisoned entry can't keep mis-mapping
    // other companies — they'll be re-resolved fresh by the model next time.
    void Remove(string? label);

    // Normalize a vendor label to its lookup key — exposed so callers can store the same normalized
    // form (e.g. on Company.FmpIndustry) that the cache keys on.
    static string Normalize(string label) =>
        new(label.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
