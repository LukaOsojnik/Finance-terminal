namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP boundary to the Python sec2md sidecar: turns one SEC filing document (built into its EDGAR
/// URL) into clean markdown, so the extractor's heading triage reads semantic titles instead of bold
/// lines scraped from raw HTML. Returns <c>null</c> on any failure (sidecar down, EDGAR error, empty
/// body) so callers fall back to the raw HTML they already fetch. Registered as a typed HttpClient.
/// </summary>
public interface ISec2MdClient
{
    Task<string?> ToMarkdownAsync(string cik, string accessionNoDashes, string primaryDocument,
        string? filingType, CancellationToken ct = default);
}
