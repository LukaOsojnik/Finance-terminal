using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Mode B extractor: read one SEC filing and propose revenue sources for human review. Fetches the
/// document, splits it into Item 1A/7/8 paragraph chunks (<see cref="FilingSections"/>), asks the
/// model to pull structured rows + verbatim proof from each, and returns de-duplicated suggestions. It
/// never writes to the database — the page fills the form and the human confirms each cell.
/// </summary>
public interface IFilingExtractionService
{
    Task<IReadOnlyList<ExtractionSuggestion>> ExtractAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        CancellationToken ct = default);

    /// <summary>The chat's grounding digest for a filing+node — cached; built by the all-sections
    /// auto-scan on a miss, or pre-populated by <see cref="ScanSelectedHeadingsAsync"/>.</summary>
    Task<string> GetOrScanDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        CancellationToken ct = default);

    /// <summary>Triage every bold heading by title, scan the AI-chosen ones in parallel (one worker
    /// each) and overwrite the chat grounding with the digest. No user picking. Returns how many
    /// sections were scanned and how many candidates were found. <paramref name="filingType"/> (the
    /// SEC form, e.g. 10-K) is passed to the sec2md sidecar.</summary>
    Task<AutoScanResult> ScanAutoAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        Action<ScanProgress>? onProgress = null, CancellationToken ct = default);
}
