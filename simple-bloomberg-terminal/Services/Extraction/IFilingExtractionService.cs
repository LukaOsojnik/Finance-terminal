using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Extraction;

/// <summary>
/// Mode B extractor: read one SEC filing and propose revenue sources for human review. Fetches the
/// document, splits it into paragraph chunks of the Items the node and form route to
/// (<see cref="FilingSections.ItemsFor"/>), asks the model to pull structured rows + verbatim proof
/// from each, and returns de-duplicated suggestions. It never writes to the database — the page fills
/// the form and the human confirms each cell.
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
    /// sections were scanned and how many candidates were found. <paramref name="filingType"/> is the
    /// SEC form (e.g. 10-K, 8-K) and selects which Items are routed to the workers — an 8-K numbers
    /// its sections on a different scheme entirely (see <see cref="FilingSections.ItemsFor"/>).</summary>
    Task<AutoScanResult> ScanAutoAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType = null,
        Action<ScanProgress>? onProgress = null, CancellationToken ct = default);
}
