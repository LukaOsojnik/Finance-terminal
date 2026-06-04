namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP-only boundary to Yahoo Finance (unofficial). Returns revenue / gross margin / currency
/// for a symbol, or <c>null</c> on any failure. Used as the non-US fallback when FMP's income
/// endpoint is premium-gated. Registered as a typed <c>HttpClient</c> with a cookie container.
/// </summary>
public interface IYahooFinanceClient
{
    Task<YahooFinancials?> GetFinancialsAsync(string symbol);
}
