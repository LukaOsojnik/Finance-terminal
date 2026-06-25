namespace simple_bloomberg_terminal.Services.Clients.Reference;

/// <summary>
/// HTTP-only boundary to ExchangeRate-API's open endpoint (open.er-api.com, no key). Secondary
/// FX source for currencies Frankfurter/ECB doesn't cover (e.g. TWD, SAR). Returns USD per one
/// unit of the currency, or <c>null</c> on failure. Registered as a typed <c>HttpClient</c>.
/// </summary>
public interface IExchangeRateApiClient
{
    Task<double?> GetUsdRateAsync(string currency);
}
