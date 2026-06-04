namespace simple_bloomberg_terminal.Services;

/// <summary>
/// HTTP-only boundary to REST Countries. Looks up one country by ISO code. Unknown code ->
/// <c>null</c>. Registered as a typed <c>HttpClient</c>. Static reference data, no key.
/// </summary>
public interface IRestCountriesClient
{
    Task<RestCountry?> GetByCodeAsync(string code);
}
