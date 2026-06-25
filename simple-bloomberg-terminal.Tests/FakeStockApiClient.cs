
namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Deterministic stand-in for <see cref="IStockApiClient"/> so the refresh flow runs offline.
/// Returns canned EDGAR facts/filings for Apple's CIK; any other CIK yields null facts
/// (simulating SEC's 404 -> the service maps that to 422 "not an SEC filer").
/// </summary>
public class FakeStockApiClient : IStockApiClient
{
    public const string AppleCik10 = "0000320193"; // matches the seeded Apple company

    public Task<EdgarCompanyFacts?> GetCompanyFacts(string cik10)
    {
        if (cik10 != AppleCik10) return Task.FromResult<EdgarCompanyFacts?>(null);

        var usGaap = new Dictionary<string, EdgarConcept>
        {
            ["Revenues"] = Concept(383_000_000_000, 2023, "2023-09-30"),
            ["CostOfRevenue"] = Concept(214_000_000_000, 2023, "2023-09-30"),
            ["OperatingExpenses"] = Concept(55_000_000_000, 2023, "2023-09-30"),
        };
        return Task.FromResult<EdgarCompanyFacts?>(new EdgarCompanyFacts(new EdgarFacts(usGaap)));
    }

    public Task<EdgarSubmissions?> GetSubmissions(string cik10)
    {
        if (cik10 != AppleCik10) return Task.FromResult<EdgarSubmissions?>(null);

        // "4" (Form 4) is intentionally ignored by the mapper -> only 2 events created.
        var recent = new EdgarRecent(
            Form: ["10-K", "8-K", "4"],
            FilingDate: ["2023-11-03", "2023-10-01", "2023-09-15"],
            ReportDate: ["2023-09-30", "", ""],
            AccessionNumber: ["0000320193-23-000106", "0000320193-23-000099", "0000320193-23-000088"],
            PrimaryDocument: ["aapl-20230930.htm", "ex99.htm", "form4.xml"],
            PrimaryDocDescription: ["10-K", "8-K", "FORM 4"]);
        return Task.FromResult<EdgarSubmissions?>(new EdgarSubmissions(new EdgarFilings(recent)));
    }

    public Task<string?> ResolveCik(string ticker) =>
        Task.FromResult(string.Equals(ticker, "AAPL", StringComparison.OrdinalIgnoreCase) ? AppleCik10 : null);

    // Reverse of ResolveCik: CIK -> ticker. Only Apple is known.
    public Task<IReadOnlyDictionary<string, string>> GetCikTickerMap() =>
        Task.FromResult<IReadOnlyDictionary<string, string>>(
            new Dictionary<string, string> { [AppleCik10] = "AAPL" });

    public Task<IReadOnlyList<EdgarTicker>> GetTickerEntries() =>
        Task.FromResult<IReadOnlyList<EdgarTicker>>(
            new[] { new EdgarTicker(320193, "AAPL", "Apple Inc.") });

    public Task<string?> GetCompanyFactsJson(string cik10) =>
        Task.FromResult<string?>(cik10 == AppleCik10
            ? "{\"cik\":320193,\"entityName\":\"Apple Inc.\",\"facts\":{\"us-gaap\":{\"Revenues\":{\"units\":{\"USD\":[{\"end\":\"2023-09-30\",\"val\":383000000000,\"fy\":2023,\"form\":\"10-K\"}]}}}}}"
            : null);

    public Task<string?> GetFilingDocument(string cik, string accessionNoDashes, string primaryDocument) =>
        Task.FromResult<string?>(
            $"FILING {cik}/{accessionNoDashes}/{primaryDocument}\nApple Inc. Form 10-K â€” total net sales 383,285 (in millions).");

    private static EdgarConcept Concept(double val, int fy, string end) =>
        new(new Dictionary<string, List<EdgarFact>>
        {
            ["USD"] = [new EdgarFact(end, val, fy, "FY", "10-K")]
        });
}
