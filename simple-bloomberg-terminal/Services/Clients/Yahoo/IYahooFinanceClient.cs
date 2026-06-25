namespace simple_bloomberg_terminal.Services.Clients.Yahoo;

/// <summary>
/// HTTP-only boundary to Yahoo Finance (unofficial). Returns revenue / gross margin / currency
/// for a symbol, or <c>null</c> on any failure. Used as the non-US fallback when FMP's income
/// endpoint is premium-gated. Registered as a typed <c>HttpClient</c> with a cookie container.
/// </summary>
public interface IYahooFinanceClient
{
    Task<YahooFinancials?> GetFinancialsAsync(string symbol);

    /// <summary>
    /// Annual income history (one row per fiscal year, newest first) for the non-US fallback when
    /// FMP is premium-gated. Yahoo only reliably reports revenue + net income here. null on any failure.
    /// </summary>
    Task<IReadOnlyList<YahooAnnualIncome>?> GetAnnualIncomeAsync(string symbol);

    /// <summary>
    /// Full weekly trading-volume history (one bar per week, oldest first) from Yahoo's chart
    /// endpoint with range=max — back to the security's first trading day. Powers the multi-year
    /// volume graph. Needs no crumb. null on any failure.
    /// </summary>
    Task<IReadOnlyList<YahooWeeklyVolume>?> GetWeeklyVolumeHistoryAsync(string symbol);
}
