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
        long companyId, string accession, string doc, ExtractionNode node, CancellationToken ct = default);

    /// <summary>The chat's grounding digest for a filing+node — cached; built by the all-sections
    /// auto-scan on a miss, or pre-populated by <see cref="ScanSelectedHeadingsAsync"/>.</summary>
    Task<string> GetOrScanDigestAsync(
        long companyId, string accession, string doc, ExtractionNode node, CancellationToken ct = default);

    /// <summary>Bold sub-headings inside the node's target Items for the user to pick from.</summary>
    Task<IReadOnlyList<HeadingInfo>> GetHeadingsAsync(
        long companyId, string accession, string doc, ExtractionNode node, CancellationToken ct = default);

    /// <summary>Scan only the user-picked headings (one worker each) and overwrite the chat grounding
    /// with the curated digest. Returns the candidate count.</summary>
    Task<int> ScanSelectedHeadingsAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<int> headingIds, CancellationToken ct = default);
}
