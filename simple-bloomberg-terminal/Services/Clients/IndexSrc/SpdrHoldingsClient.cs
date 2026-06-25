using System.Globalization;
using System.IO.Compression;
using System.Xml.Linq;

namespace simple_bloomberg_terminal.Services.Clients.IndexSrc;

/// <summary>One holding parsed from a SPDR daily-holdings file: its ticker and the fund weight in
/// percent (e.g. 7.12 = 7.12%). Weight is null when the row omitted it (e.g. the trailing cash line).
/// The file also has a Sector column but SSGA leaves it blank ("-") for equity funds, so it is not
/// read â€” the index's sector is inferred from its matched members instead.</summary>
public record SpdrHolding(string Ticker, double? WeightPct);

/// <summary>A parsed SPDR holdings file: the fund's display name (when the header block carried it)
/// and its holdings. <see cref="EtfTicker"/> echoes the requested ETF symbol.</summary>
public record SpdrHoldings(string EtfTicker, string? FundName, IReadOnlyList<SpdrHolding> Holdings);

/// <summary>
/// HTTP-only boundary to State Street's free, no-auth SPDR daily-holdings files. Unlike Wikipedia
/// (membership only), these carry the fund's REAL published weight AND a per-holding GICS sector, so an
/// index imported this way gets accurate weights and a free sector classification. The file is XLSX â€”
/// a ZIP of OOXML â€” parsed here with framework-only <see cref="ZipArchive"/> + <see cref="XDocument"/>
/// (no spreadsheet dependency). A non-SPDR ticker 404s, which the caller treats as "fall back to
/// Wikipedia".
/// </summary>
public interface ISpdrHoldingsClient
{
    // etfTicker is a SPDR ETF symbol, e.g. "SPY", "DIA", "XLK". Throws HttpRequestException on a
    // non-SPDR ticker (404) so the import pipeline can fall back to the Wikipedia source.
    Task<SpdrHoldings> GetHoldingsAsync(string etfTicker, CancellationToken ct = default);
}

public class SpdrHoldingsClient : ISpdrHoldingsClient
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private readonly HttpClient _http;

    public SpdrHoldingsClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<SpdrHoldings> GetHoldingsAsync(string etfTicker, CancellationToken ct = default)
    {
        var ticker = etfTicker.Trim().ToLowerInvariant();
        var path = $"/library-content/products/fund-data/etfs/us/holdings-daily-us-en-{ticker}.xlsx";

        // Buffer to a MemoryStream: ZipArchive needs a seekable stream to read the central directory,
        // and the HTTP content stream isn't seekable.
        var bytes = await _http.GetByteArrayAsync(path, ct);
        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        var shared = ReadSharedStrings(zip);
        var rows = ReadSheetRows(zip, shared);

        // Locate the header row by its labels (the file starts with a metadata block, so the table
        // header isn't at a fixed offset). Columns are then read by label, not position.
        int headerIdx = -1, tickerCol = -1, weightCol = -1;
        for (var r = 0; r < rows.Count; r++)
        {
            var cells = rows[r];
            int t = Find(cells, "ticker"), w = Find(cells, "weight");
            if (t >= 0 && w >= 0)
            {
                headerIdx = r; tickerCol = t; weightCol = w;
                break;
            }
        }

        var fundName = FindFundName(rows, headerIdx);
        if (headerIdx < 0) return new SpdrHoldings(etfTicker.ToUpperInvariant(), fundName, []);

        var holdings = new List<SpdrHolding>();
        for (var r = headerIdx + 1; r < rows.Count; r++)
        {
            var cells = rows[r];
            var tk = NormalizeTicker(Get(cells, tickerCol));
            if (string.IsNullOrEmpty(tk)) continue;   // blank ticker => footer/cash/disclaimer rows

            double? weight = double.TryParse(Get(cells, weightCol), NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
            holdings.Add(new SpdrHolding(tk, weight));
        }

        Normalize(holdings);
        return new SpdrHoldings(etfTicker.ToUpperInvariant(), fundName, holdings);
    }

    // SSGA stores Weight either as a percentage number (7.12) or a fraction (0.0712). Detect by the
    // total: a fraction set sums to ~1, a percentage set to ~100. Scale fractions up so every weight
    // is a percent, matching IndexConstituent.WeightPct used everywhere else.
    private static void Normalize(List<SpdrHolding> holdings)
    {
        var sum = holdings.Sum(h => h.WeightPct ?? 0);
        if (sum > 0 && sum <= 2)
            for (var i = 0; i < holdings.Count; i++)
                if (holdings[i].WeightPct is { } w)
                    holdings[i] = holdings[i] with { WeightPct = w * 100 };
    }

    // Pull <sst><si>â€¦</si></sst> into an index-aligned table; concatenate the <t> runs inside each <si>.
    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        return doc.Root!.Elements(Ns + "si")
            .Select(si => string.Concat(si.Descendants(Ns + "t").Select(t => t.Value)))
            .ToList();
    }

    // Read the first worksheet into rows of (column-index -> text), resolving shared-string cells.
    private static List<Dictionary<int, string>> ReadSheetRows(ZipArchive zip, List<string> shared)
    {
        var entry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal)
                                                    && e.FullName.EndsWith(".xml", StringComparison.Ordinal));
        if (entry is null) return [];
        using var s = entry.Open();
        var doc = XDocument.Load(s);

        var rows = new List<Dictionary<int, string>>();
        foreach (var row in doc.Descendants(Ns + "row"))
        {
            var cells = new Dictionary<int, string>();
            foreach (var c in row.Elements(Ns + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r"));
                if (col < 0) continue;
                var type = (string?)c.Attribute("t");
                string? value = type == "inlineStr"
                    ? string.Concat(c.Descendants(Ns + "t").Select(t => t.Value))
                    : c.Element(Ns + "v")?.Value;
                if (value is null) continue;
                if (type == "s" && int.TryParse(value, out var idx) && idx >= 0 && idx < shared.Count)
                    value = shared[idx];
                cells[col] = value;
            }
            rows.Add(cells);
        }
        return rows;
    }

    // The metadata block lists "Fund Name:" with the value in the next cell of the same row.
    private static string? FindFundName(List<Dictionary<int, string>> rows, int headerIdx)
    {
        var limit = headerIdx < 0 ? rows.Count : headerIdx;
        for (var r = 0; r < limit; r++)
        {
            var cells = rows[r];
            var labelCol = cells.Where(kv => kv.Value.Contains("fund name", StringComparison.OrdinalIgnoreCase))
                                .Select(kv => (int?)kv.Key).FirstOrDefault();
            if (labelCol is { } lc)
            {
                var value = cells.Where(kv => kv.Key > lc && !string.IsNullOrWhiteSpace(kv.Value))
                                 .OrderBy(kv => kv.Key).Select(kv => kv.Value).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
            }
        }
        return null;
    }

    // Index (0-based) of the first cell whose text contains `label` (case-insensitive), or -1.
    private static int Find(Dictionary<int, string> cells, string label) =>
        cells.Where(kv => kv.Value.Contains(label, StringComparison.OrdinalIgnoreCase))
             .Select(kv => kv.Key).DefaultIfEmpty(-1).Min();

    private static string? Get(Dictionary<int, string> cells, int col) =>
        col >= 0 && cells.TryGetValue(col, out var v) ? v : null;

    // "A1"/"AB12" -> 0-based column index from the leading letters; -1 if no ref.
    private static int ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef)) return -1;
        var col = 0; var any = false;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z') { col = col * 26 + (ch - 'A' + 1); any = true; }
            else if (ch is >= 'a' and <= 'z') { col = col * 26 + (ch - 'a' + 1); any = true; }
            else break;
        }
        return any ? col - 1 : -1;
    }

    // Match the Wikipedia client: dot share-class -> dash (BRK.B -> BRK-B), drop footnotes, upper-case.
    private static string NormalizeTicker(string? raw)
    {
        var token = (raw ?? "").Split([' ', '\t', '\n', '['], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return token.Replace('.', '-').ToUpperInvariant();
    }
}
