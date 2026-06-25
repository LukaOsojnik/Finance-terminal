using HtmlAgilityPack;

namespace simple_bloomberg_terminal.Services.Clients.IndexSrc;

/// <summary>One index member parsed from a Wikipedia constituents table: its ticker and, when the
/// table carries it (S&amp;P 500 does, NASDAQ-100/Dow don't), its SEC CIK.</summary>
public record WikiConstituent(string Ticker, string? Cik);

/// <summary>
/// Free membership source: scrapes the "constituents" table off a Wikipedia index page (S&amp;P 500,
/// NASDAQ-100, Dow). FMP's constituent endpoint is premium (402), so this replaces it. Columns are
/// located by HEADER TEXT (Symbol/Ticker, CIK) rather than fixed positions, since the three pages
/// order their columns differently. Wikipedia blocks blank user-agents, so one is set in config.
/// </summary>
public class WikipediaIndexClient : IWikipediaIndexClient
{
    private readonly HttpClient _http;

    public WikipediaIndexClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<List<WikiConstituent>> GetConstituentsAsync(string pagePath)
    {
        var html = await _http.GetStringAsync(pagePath);
        var doc = new HtmlDocument();
        doc.LoadHtml(html);

        // The members table has id="constituents" on all three pages; fall back to the first wikitable.
        var table = doc.GetElementbyId("constituents")
            ?? doc.DocumentNode.SelectSingleNode("//table[contains(@class,'wikitable')]");
        if (table is null) return [];

        var headers = table.SelectNodes(".//tr[1]/th");
        if (headers is null) return [];

        int tickerCol = -1, cikCol = -1;
        for (var i = 0; i < headers.Count; i++)
        {
            var h = Clean(headers[i].InnerText).ToLowerInvariant();
            if (tickerCol < 0 && (h.Contains("symbol") || h.Contains("ticker"))) tickerCol = i;
            if (cikCol < 0 && h.Contains("cik")) cikCol = i;
        }
        if (tickerCol < 0) return [];

        var rows = table.SelectNodes(".//tr[position()>1]");
        if (rows is null) return [];

        var result = new List<WikiConstituent>();
        foreach (var row in rows)
        {
            // Include th + td in document order: some tables (Dow) make the first data column a row-header
            // <th>, so a td-only read would shift every column and misalign with the header indices.
            var cells = row.SelectNodes("./th|./td");
            if (cells is null || cells.Count <= tickerCol) continue;

            var ticker = NormalizeTicker(Clean(cells[tickerCol].InnerText));
            if (string.IsNullOrEmpty(ticker)) continue;

            var cik = cikCol >= 0 && cells.Count > cikCol ? Cik.Normalize(Clean(cells[cikCol].InnerText)) : null;
            result.Add(new WikiConstituent(ticker, cik));
        }
        return result;
    }

    private static string Clean(string s) => HtmlEntity.DeEntitize(s ?? "").Trim();

    // Wikipedia writes share-class tickers with a dot (BRK.B); the SEC ticker map uses a dash (BRK-B).
    // Also drop any trailing footnote/bracket text and take the first whitespace-delimited token.
    private static string NormalizeTicker(string raw)
    {
        var token = raw.Split([' ', '\t', '\n', '['], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return token.Replace('.', '-').ToUpperInvariant();
    }
}
