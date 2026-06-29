using System.ComponentModel;
using ModelContextProtocol.Server;

namespace McpServer.Tools;

// Read-only stock tools over the terminal's API. Each tool covers ONE logical category so a weak model
// pulls only the slice it needs and never loads a whole company into context at once. Every result
// reports an explicit status (present / missing / unavailable / not_found / error) — the model must base
// "this data is missing" on that status, not on an empty list it might otherwise hallucinate around.
[McpServerToolType]
public sealed class StockTools(TerminalClient terminal)
{
    // Keep payloads small for weak models: cap the long series, report the true totals alongside.
    private const int VolumeWeeksCap = 52;
    private const int FilingsCap = 25;

    [McpServerTool(Name = "find_company"), Description(
        "Resolve a company to its terminal companyId. ALWAYS call this first — every other stock tool " +
        "needs the companyId it returns. Matches a company name, ticker, or CIK by prefix. status " +
        "'no_match' means the company is not tracked in the terminal, so no data exists for it.")]
    public async Task<FindCompanyResult> FindCompany(
        [Description("Company name, ticker, or CIK prefix, e.g. 'Apple', 'NVIDIA', 'AAPL'.")] string query,
        CancellationToken ct)
    {
        var matches = await terminal.SearchCompanies(query, ct);
        if (matches.Count == 0)
            return new FindCompanyResult("no_match",
                $"No tracked company matches '{query}'. The terminal only holds companies that have been added to it.",
                []);

        var list = matches.Select(c => new CompanyMatch(c.Id, c.Name, c.Cik, c.SectorName, c.Country?.Name)).ToList();
        return new FindCompanyResult("present",
            $"{list.Count} match(es); pass a companyId to the other get_stock_* tools.", list);
    }

    [McpServerTool(Name = "get_stock_profile"), Description(
        "Company identity and GICS classification (sector / industry / sub-industry names) plus headline " +
        "figures. 'missingFields' lists fields the terminal has no value for — report those as unknown " +
        "rather than guessing a value.")]
    public async Task<ProfileResult> GetStockProfile(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var c = await terminal.GetCompany(companyId, ct);
        if (c is null) return new ProfileResult("not_found", $"No company with id {companyId} is tracked in the terminal.", null);

        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(c.Cik)) missing.Add("cik");
        if (c.SectorName is null) missing.Add("sector");
        if (c.IndustryName is null) missing.Add("industry");
        if (c.SubIndustry is null) missing.Add("subIndustry");
        if (c.RevenueTotal is null) missing.Add("revenueTotal");
        if (c.GrossMargin is null) missing.Add("grossMargin");
        if (c.Country is null) missing.Add("country");
        if (c.AsOf is null) missing.Add("asOf");

        var profile = new StockProfile(
            c.Id, c.Name, c.Cik,
            new Classification(c.SectorName, c.IndustryName, c.SubIndustry),
            c.RevenueTotal, c.GrossMargin, c.AsOf?.ToString("yyyy-MM-dd"),
            c.Country?.Name, c.Country?.Code, c.Notes, missing);

        var note = missing.Count == 0 ? "Full profile present." : $"Present; no value for: {string.Join(", ", missing)}.";
        return new ProfileResult("present", note, profile);
    }

    [McpServerTool(Name = "get_stock_financials"), Description(
        "Dated fiscal financial history (revenue, margins, income, cash flow), newest period first. " +
        "status 'missing' means no financials are loaded for this tracked company.")]
    public async Task<FinancialsResult> GetStockFinancials(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var c = await terminal.GetCompany(companyId, ct);
        if (c is null) return new FinancialsResult("not_found", $"No company with id {companyId} is tracked.", false, null, []);

        var rows = (await terminal.SearchFinancials(c.Name, ct))
            .Where(f => f.CompanyId == companyId)
            .OrderByDescending(f => f.FiscalYear).ThenByDescending(f => f.Period)
            .ToList();
        if (rows.Count == 0)
            return new FinancialsResult("missing", "No financial history is loaded for this company yet.", false, null, []);

        var newest = rows.Max(f => f.CapturedAt);
        var stale = newest < DateTime.UtcNow.AddDays(-400);
        var periods = rows.Select(f => new FinancialPeriod(
            f.FiscalYear, f.Period.ToString(), f.EndDate?.ToString("yyyy-MM-dd"), f.ReportedCurrency, f.Source.ToString(),
            f.Revenue, f.GrossProfit, f.OperatingIncome, f.Ebitda, f.NetIncome, f.Eps, f.GrossMargin, f.NetMargin, f.FreeCashFlow)).ToList();

        var note = stale ? $"Present but possibly stale — newest captured {newest:yyyy-MM-dd}." : "Present.";
        return new FinancialsResult("present", note, stale, newest.ToString("yyyy-MM-dd"), periods);
    }

    [McpServerTool(Name = "get_stock_risks"), Description(
        "Disclosed risk factors (from SEC Item 1A / 7A), each tagged with a scope. status 'missing' means " +
        "no risk factors have been extracted for this tracked company.")]
    public async Task<RisksResult> GetStockRisks(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var c = await terminal.GetCompany(companyId, ct);
        if (c is null) return new RisksResult("not_found", $"No company with id {companyId} is tracked.", 0, []);

        var risks = (await terminal.SearchRisks(c.Name, ct))
            .Where(r => r.CompanyId == companyId)
            .Select(r => new RiskItem(r.Scope.ToString(), r.Name, r.Note)).ToList();
        if (risks.Count == 0)
            return new RisksResult("missing", "No risk factors are extracted for this company yet.", 0, []);

        return new RisksResult("present", $"{risks.Count} risk factor(s).", risks.Count, risks);
    }

    [McpServerTool(Name = "get_stock_relationships"), Description(
        "The company's revenue sources (customers / segments / regions / products) and cost sources " +
        "(suppliers / cost lines), each with any linked counterparty companyId. status 'missing' means " +
        "none are recorded.")]
    public async Task<RelationshipsResult> GetStockRelationships(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var c = await terminal.GetCompany(companyId, ct);
        if (c is null) return new RelationshipsResult("not_found", $"No company with id {companyId} is tracked.", [], []);

        var revenue = (c.RevenueSources ?? []).Select(r =>
            new Counterparty(r.SourceType.ToString(), r.Name, r.Value, r.Percentage, r.RelatedCompanyId)).ToList();
        var costs = (c.CostSources ?? []).Select(r =>
            new Counterparty(r.CostBase.ToString(), r.Name, r.Value, r.Percentage, r.RelatedCompanyId)).ToList();

        if (revenue.Count == 0 && costs.Count == 0)
            return new RelationshipsResult("missing", "No revenue or cost sources are recorded for this company.", [], []);

        return new RelationshipsResult("present",
            $"{revenue.Count} revenue source(s), {costs.Count} cost source(s).", revenue, costs);
    }

    [McpServerTool(Name = "get_stock_volume"), Description(
        "Weekly trading-volume history (the most recent weeks; totalWeeks is the full stored count). " +
        "status 'missing' means no volume has been ingested for this company.")]
    public async Task<VolumeResult> GetStockVolume(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var series = await terminal.GetVolume(companyId, ct);
        if (series is null) return new VolumeResult("not_found", $"No company with id {companyId} is tracked.", false, null, 0, []);
        if (series.Count == 0) return new VolumeResult("missing", "No weekly volume has been ingested for this company.", false, null, 0, []);

        var ordered = series.OrderBy(p => p.WeekStart).ToList();
        var latest = ordered[^1].WeekStart;
        var stale = DateOnly.TryParse(latest, out var lw)
            && lw < DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-30));
        var recent = ordered.TakeLast(VolumeWeeksCap).Select(p => new VolumePoint(p.WeekStart, p.Volume)).ToList();

        var note = stale
            ? $"Present but latest bar is {latest} — may be stale."
            : $"Present; showing last {recent.Count} of {ordered.Count} weeks.";
        return new VolumeResult("present", note, stale, latest, ordered.Count, recent);
    }

    [McpServerTool(Name = "get_stock_filings"), Description(
        "Recent SEC EDGAR filings (form, date, accession number, document URL), fetched live from SEC. " +
        "status 'unavailable' means the company is not an SEC filer (no CIK); status 'error' means SEC " +
        "could not be reached.")]
    public async Task<FilingsResult> GetStockFilings(
        [Description("companyId from find_company.")] long companyId,
        CancellationToken ct)
    {
        var fetch = await terminal.GetFilings(companyId, ct);
        switch ((int)fetch.Status)
        {
            case 200:
                var items = (fetch.Filings ?? []).Take(FilingsCap)
                    .Select(f => new FilingItem(f.Form, f.FilingDate, f.AccessionNumber, f.DocumentUrl)).ToList();
                return items.Count == 0
                    ? new FilingsResult("missing", "SEC returned no filings for this company.", 0, [])
                    : new FilingsResult("present", $"{items.Count} recent filing(s).", items.Count, items);
            case 404:
                return new FilingsResult("not_found", $"No company with id {companyId} is tracked.", 0, []);
            case 409:
                return new FilingsResult("unavailable",
                    "This company has no SEC CIK — it is not an SEC filer, so EDGAR filings do not exist for it.", 0, []);
            case 422:
                return new FilingsResult("missing", "SEC has no filings listed for this company.", 0, []);
            case 503:
                return new FilingsResult("error", "SEC EDGAR could not be reached right now; try again later.", 0, []);
            default:
                return new FilingsResult("error", $"Unexpected response from the terminal ({(int)fetch.Status}).", 0, []);
        }
    }
}
