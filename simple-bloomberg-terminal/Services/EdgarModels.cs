using System.Text.Json.Serialization;

namespace simple_bloomberg_terminal.Services;

// Minimal shapes for the SEC EDGAR JSON we actually read. System.Net.Http.Json uses
// JsonSerializerDefaults.Web (camelCase, case-insensitive), so only the keys that don't
// match a C# property name by case (hyphens, snake_case) need an explicit attribute.

// /api/xbrl/companyfacts/CIK{cik10}.json
public record EdgarCompanyFacts(EdgarFacts? Facts);

public record EdgarFacts(
    [property: JsonPropertyName("us-gaap")] Dictionary<string, EdgarConcept>? UsGaap);

public record EdgarConcept(Dictionary<string, List<EdgarFact>>? Units);

public record EdgarFact(string? End, double? Val, int? Fy, string? Fp, string? Form);

// /submissions/CIK{cik10}.json (parallel arrays under filings.recent)
public record EdgarSubmissions(EdgarFilings? Filings);

public record EdgarFilings(EdgarRecent? Recent);

// Parallel arrays: index i describes one filing. SEC keys are camelCase => no attrs needed.
// Extra fields beyond Form/FilingDate are optional so existing call sites stay valid.
public record EdgarRecent(
    List<string>? Form,
    List<string>? FilingDate,
    List<string>? ReportDate = null,
    List<string>? AccessionNumber = null,
    List<string>? PrimaryDocument = null,
    List<string>? PrimaryDocDescription = null);

// https://www.sec.gov/files/company_tickers.json (numeric-string-keyed map)
public record EdgarTicker(
    [property: JsonPropertyName("cik_str")] long CikStr,
    string Ticker,
    string Title);
