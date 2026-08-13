using System.Text.RegularExpressions;

namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>One entry from a filing's <c>FilingSummary.xml</c> report index: the SEC-rendered
/// statement/note file, its title, and the menu group it sits under ("Cover", "Statements", "Notes",
/// "Tables", "Details").</summary>
public record ReportEntry(string File, string ShortName, string Category);

/// <summary>A selected report, fetched. <paramref name="Html"/> is the raw R*.htm body.</summary>
public record FilingReport(string ShortName, string Category, string Html);

/// <summary>
/// Reads the SEC's own rendered reports for a filing — the <c>R1.htm, R2.htm, …</c> files EDGAR
/// generates from the submitted XBRL and serves in its "Financial Report" viewer.
///
/// These matter because they are the financial statements already reconstructed as well-formed HTML
/// tables: one statement per file, real <c>&lt;th&gt;</c> headers, units stated in the title
/// ("USD ($) … $ in Millions"), and every label cell carrying its us-gaap concept name. Parsing the
/// statements back out of the main 10-K document is guesswork by comparison, so Item 8 reads these
/// instead. Best-effort throughout: a filing with no FilingSummary (older filings predate it) simply
/// yields no reports and the caller falls back to the document itself.
/// </summary>
public interface IFilingReportReader
{
    /// <summary>The reports matching <paramref name="select"/>, fetched in index order. The predicate
    /// runs before any fetch — a filing indexes ~60 reports and each is its own SEC request.</summary>
    Task<IReadOnlyList<FilingReport>> ReportsAsync(
        string cikTrimmed, string accessionNoDashes, Func<ReportEntry, bool> select,
        CancellationToken ct = default);
}

public class FilingReportReader(IStockApiClient client) : IFilingReportReader
{
    /// <summary>Ceiling on reports fetched for one filing, so a pathological index can't fan out into
    /// hundreds of SEC requests. The selector is expected to pick far fewer than this.</summary>
    private const int MaxReports = 20;

    public async Task<IReadOnlyList<FilingReport>> ReportsAsync(
        string cikTrimmed, string accessionNoDashes, Func<ReportEntry, bool> select,
        CancellationToken ct = default)
    {
        var index = await IndexAsync(cikTrimmed, accessionNoDashes, ct);
        var wanted = index.Where(select).Take(MaxReports).ToList();

        var result = new List<FilingReport>();
        foreach (var entry in wanted)
        {
            ct.ThrowIfCancellationRequested();
            var html = await FetchAsync(cikTrimmed, accessionNoDashes, entry.File, ct);
            if (!string.IsNullOrWhiteSpace(html))
                result.Add(new FilingReport(entry.ShortName, entry.Category, html!));
        }
        return result;
    }

    /// <summary>The filing's report index. Empty when FilingSummary.xml is missing or unparseable.</summary>
    private async Task<IReadOnlyList<ReportEntry>> IndexAsync(
        string cikTrimmed, string accessionNoDashes, CancellationToken ct)
    {
        var xml = await FetchAsync(cikTrimmed, accessionNoDashes, "FilingSummary.xml", ct);
        return string.IsNullOrWhiteSpace(xml) ? [] : ParseIndex(xml!);
    }

    /// <summary>
    /// Pure parse (no I/O), so it's unit-testable against a fixture — same split as
    /// <see cref="XbrlInstanceReader.ParseSegmentCosts"/>. Regex rather than an XML reader because
    /// FilingSummary.xml is flat and some filers emit it with undeclared entities that XDocument rejects.
    /// </summary>
    public static IReadOnlyList<ReportEntry> ParseIndex(string xml)
    {
        var result = new List<ReportEntry>();
        foreach (Match m in Regex.Matches(xml, "(?s)<Report[^>]*>(.*?)</Report>"))
        {
            var body = m.Groups[1].Value;
            var file = Tag(body, "HtmlFileName");
            // A report with no HtmlFileName is an XLSX-only sheet (some filers emit these); skip it.
            if (file.Length == 0) continue;
            result.Add(new ReportEntry(file, Tag(body, "ShortName"), Tag(body, "MenuCategory")));
        }
        return result;
    }

    private static string Tag(string body, string name) =>
        System.Net.WebUtility.HtmlDecode(
            Regex.Match(body, $"<{name}>(.*?)</{name}>", RegexOptions.Singleline).Groups[1].Value).Trim();

    // Guarded like XbrlInstanceReader's fetches: a missing or unreachable file yields null rather than
    // sinking the scan the caller is waiting on.
    private async Task<string?> FetchAsync(
        string cikTrimmed, string accessionNoDashes, string file, CancellationToken ct)
    {
        try
        {
            return await client.GetFilingDocument(cikTrimmed, accessionNoDashes, file);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
