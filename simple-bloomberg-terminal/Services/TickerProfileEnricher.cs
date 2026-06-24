using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// The shared FMP-by-ticker enrichment kernel: given an FMP profile (+ optional income), map it to a
/// <see cref="CompanyCreateModel"/> and fill the fields that aren't on the raw payload — a fetch-date
/// "as of", the GICS industry (static label map, then LLM on a miss), and Yahoo-sourced revenue/margin
/// when FMP income is premium-gated. Extracted so the New Company fetch (<c>CompaniesController</c>)
/// and the counterparty link (<c>ExtractionController</c>) share one path instead of two copies that
/// drift apart — the link copy had silently dropped the AsOf + industry steps. Each caller keeps its
/// own FMP fetch/error policy, country resolution, and (form-only) ViewBag notes.
/// </summary>
public interface ITickerProfileEnricher
{
    Task<TickerProfileEnrichment> BuildModelAsync(FmpProfile profile, FmpIncome? income, string ticker);
}

/// <param name="Model">The enriched create-model (industry / AsOf / financials filled).</param>
/// <param name="FinancialsNote">A user-facing note about the financials source (Yahoo fallback, or
/// non-USD revenue left blank), or null. The form surfaces it via ViewBag; the link path ignores it.</param>
public record TickerProfileEnrichment(CompanyCreateModel Model, string? FinancialsNote);

public class TickerProfileEnricher : ITickerProfileEnricher
{
    private readonly IIndustryClassifier _industryClassifier;
    private readonly IYahooFinanceClient _yahoo;
    private readonly IExchangeRateApiClient _exchangeRate;

    public TickerProfileEnricher(IIndustryClassifier industryClassifier, IYahooFinanceClient yahoo,
        IExchangeRateApiClient exchangeRate)
    {
        _industryClassifier = industryClassifier;
        _yahoo = yahoo;
        _exchangeRate = exchangeRate;
    }

    public async Task<TickerProfileEnrichment> BuildModelAsync(FmpProfile profile, FmpIncome? income, string ticker)
    {
        var model = FmpMapper.ToCreateModel(profile, income);
        model.Symbol = ticker;   // carried hidden into the Create POST to re-fetch financial history

        // Stamp the fetch date so the row always has an "as of" without manual entry (FMP income is
        // often premium-gated -> null, which would otherwise leave AsOf unset).
        model.AsOf = DateOnly.FromDateTime(DateTime.Today);

        // Resolve the GICS sub-industry from the vendor label (cached, else cheap LLM) and roll it up to
        // the Industry tier so the user doesn't have to pick either.
        model.GicsSubIndustry = await _industryClassifier.ResolveSubIndustryAsync(model.Sector, model.FmpIndustry, model.Name);
        model.Industry = model.GicsSubIndustry?.GetIndustry();

        string? note = null;
        if (income == null)
            note = await ApplyYahooFinancials(model, ticker);
        else if (!string.Equals(income.ReportedCurrency, "USD", StringComparison.OrdinalIgnoreCase))
            note = $"Revenue is reported in {income.ReportedCurrency}; left blank — enter the USD value manually.";

        return new TickerProfileEnrichment(model, note);
    }

    // Backfill revenue (USD) + gross margin from Yahoo when FMP income is unavailable. Margin is a
    // currency-agnostic ratio so it's always filled; revenue is converted to USD via ExchangeRate-API
    // and left blank if no rate is available. Returns a user-facing note describing what happened.
    private async Task<string?> ApplyYahooFinancials(CompanyCreateModel model, string ticker)
    {
        var yf = await _yahoo.GetFinancialsAsync(ticker);
        if (yf == null)
            return "Financials aren't available for this symbol — enter revenue and gross margin manually.";

        if (yf.GrossMargins is { } gm)
            model.GrossMargin = Math.Round(gm, 2); // 2 dp to satisfy the form's step="0.01"

        if (yf.Revenue is { } rev && !string.IsNullOrWhiteSpace(yf.Currency))
        {
            var rate = await _exchangeRate.GetUsdRateAsync(yf.Currency);
            if (rate is { } r)
                model.RevenueTotal = Math.Round(rev * r);
            else
                return $"Revenue is in {yf.Currency} (no USD conversion rate available) — enter it manually.";
        }

        return "Financials filled from Yahoo Finance (FMP is premium-gated for this symbol).";
    }
}
