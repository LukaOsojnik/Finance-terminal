using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>
/// Typed HttpClient for the sec2md sidecar (see <c>sec2md-service/</c>). Builds the EDGAR document URL
/// from the same CIK / accession / primary-document parts the SEC fetch already uses, POSTs it, and
/// returns the markdown. Any transport or non-success response collapses to <c>null</c> — the caller
/// then uses raw HTML, so a stopped sidecar degrades the pipeline rather than breaking it.
/// </summary>
public class Sec2MdClient : ISec2MdClient
{
    private readonly HttpClient _http;
    private readonly string _dumpDir;   // filings/ under the content root — converted markdown is saved here
    private readonly ILogger<Sec2MdClient> _logger;

    public Sec2MdClient(HttpClient http, IWebHostEnvironment env, ILogger<Sec2MdClient> logger)
    {
        _http = http;
        _dumpDir = Path.Combine(env.ContentRootPath, "filings");
        _logger = logger;
    }

    public async Task<string?> ToMarkdownAsync(
        string cik, string accessionNoDashes, string primaryDocument, string? filingType,
        CancellationToken ct = default)
    {
        // EDGAR archive URL — sec2md.parse_filing fetches this itself (the sidecar adds the User-Agent).
        var url = $"https://www.sec.gov/Archives/edgar/data/{cik}/{accessionNoDashes}/{primaryDocument}";
        try
        {
            var resp = await _http.PostAsJsonAsync("/convert", new ConvertRequest(url, filingType), ct);
            if (!resp.IsSuccessStatusCode)
            {
                _logger.LogWarning("sec2md convert failed for {Url}: {Status}", url, (int)resp.StatusCode);
                return null;
            }
            var body = await resp.Content.ReadFromJsonAsync<ConvertResponse>(ct);
            if (string.IsNullOrWhiteSpace(body?.Markdown)) return null;

            await DumpAsync(cik, accessionNoDashes, primaryDocument, body!.Markdown, ct);
            return body.Markdown;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "sec2md sidecar unreachable for {Cik}/{Accession}", cik, accessionNoDashes);
            return null;   // sidecar unreachable / timed out → caller falls back to raw HTML
        }
    }

    // Save the converted markdown under filings/ so the parsing can be eyeballed against the source
    // filing. Best-effort: a disk failure must never sink the conversion the caller is waiting on.
    private async Task DumpAsync(
        string cik, string accession, string doc, string markdown, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(_dumpDir);
            var name = $"{cik}_{accession}_{doc}.md";
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            await File.WriteAllTextAsync(Path.Combine(_dumpDir, name), markdown, ct);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* best-effort */ }
    }

    private record ConvertRequest(
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("filing_type")] string? FilingType);

    private record ConvertResponse([property: JsonPropertyName("markdown")] string Markdown);
}
