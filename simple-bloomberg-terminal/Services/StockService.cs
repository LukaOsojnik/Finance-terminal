using AutoMapper;
using simple_bloomberg_terminal.Dtos;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// The one place with real logic: pull SEC EDGAR JSON, map it onto the graph child rows
/// (RevenueSource / CostSource) tagged <see cref="DataSource.EDGAR"/>, persist via the shared
/// repos, and decide failure codes. Hand-entered (MANUAL) rows are never touched. Filings are
/// not ingested here — they become <see cref="Filing"/> rows only when a user references one in
/// the extraction UI (see ExtractionController), so a plain refresh never creates filing rows.
/// </summary>
public class StockService(
    IStockApiClient client,
    ICompanyRepository companies,
    IRevenueSourceRepository revenue,
    ICostSourceRepository cost,
    IMapper mapper) : IStockService
{
    public async Task<CompanyDto> RefreshAsync(Company company)
    {
        var cik10 = company.Cik!.PadLeft(10, '0');

        EdgarCompanyFacts? facts;
        try
        {
            facts = await client.GetCompanyFacts(cik10);
        }
        catch (HttpRequestException ex)
        {
            throw new EdgarException(StatusCodes.Status503ServiceUnavailable, $"SEC unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            throw new EdgarException(StatusCodes.Status503ServiceUnavailable, "SEC request timed out.");
        }

        if (facts is null)
            throw new EdgarException(StatusCodes.Status422UnprocessableEntity, $"CIK {cik10} is not an SEC filer.");

        // Idempotency: wipe only this company's prior EDGAR rows, then reinsert.
        revenue.ClearByCompanyAndDataSource(company.Id, DataSource.EDGAR);
        cost.ClearByCompanyAndDataSource(company.Id, DataSource.EDGAR);

        MapRevenue(facts.Facts, company.Id);
        MapCosts(facts.Facts, company.Id);

        var refreshed = companies.GetWithGraphRelations(company.Id);
        return mapper.Map<CompanyDto>(refreshed);
    }

    private void MapRevenue(EdgarFacts? facts, long companyId)
    {
        var rev = LatestAnnual(facts, "Revenues", "RevenueFromContractWithCustomerExcludingAssessedTax");
        if (rev is null) return;
        revenue.Add(new RevenueSource(SourceType.SEGMENT, $"Revenue {PeriodLabel(rev)}", companyId)
            { Value = rev.Val, DataSource = DataSource.EDGAR });
    }

    private void MapCosts(EdgarFacts? facts, long companyId)
    {
        var cogs = LatestAnnual(facts, "CostOfRevenue", "CostOfGoodsAndServicesSold");
        if (cogs is not null)
            cost.Add(new CostSource(CostBase.COGS, $"COGS {PeriodLabel(cogs)}", companyId)
                { Value = cogs.Val, DataSource = DataSource.EDGAR });

        var opex = LatestAnnual(facts, "OperatingExpenses");
        if (opex is not null)
            cost.Add(new CostSource(CostBase.OPEX, $"OPEX {PeriodLabel(opex)}", companyId)
                { Value = opex.Val, DataSource = DataSource.EDGAR });
    }

    // Most recent full-year (10-K) USD data point for the first concept name that has one.
    private static EdgarFact? LatestAnnual(EdgarFacts? facts, params string[] names)
    {
        if (facts?.UsGaap is null) return null;
        foreach (var name in names)
        {
            if (!facts.UsGaap.TryGetValue(name, out var concept)) continue;
            if (concept.Units is null || !concept.Units.TryGetValue("USD", out var points)) continue;
            var p = points
                .Where(x => x.Form == "10-K" && x.Val.HasValue && x.End != null)
                .OrderByDescending(x => x.End)
                .FirstOrDefault();
            if (p is not null) return p;
        }
        return null;
    }

    private static string PeriodLabel(EdgarFact f) =>
        f.Fy?.ToString() ?? (f.End is { Length: >= 4 } e ? e[..4] : "n/a");
}
