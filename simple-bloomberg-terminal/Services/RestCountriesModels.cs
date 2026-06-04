namespace simple_bloomberg_terminal.Services;

// Minimal shapes for the REST Countries /v3.1/alpha JSON we read. Web defaults bind the
// camelCase keys. `currencies` is an object keyed by currency code -> we take the first key.
public record RestCountry(
    RestCountryName? Name,
    Dictionary<string, RestCurrency>? Currencies,
    string? Cca2,
    string? Cca3,
    string? Region,
    long? Population);

public record RestCountryName(string? Common, string? Official);

public record RestCurrency(string? Name, string? Symbol);
