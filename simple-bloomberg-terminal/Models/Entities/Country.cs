using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.Entities;

public class Country
{
    public Country(string code, string name, string region, string currencyCode)
    {
        Code = code;
        Name = name;
        Region = region;
        CurrencyCode = currencyCode;
    }

    [Key]
    public long Id { get; set; }
    public string Code { get; set; }
    public string Name { get; set; }
    public string Region { get; set; }
    public string CurrencyCode { get; set; }
    public double? GdpUsd { get; set; }
    public long? Population { get; set; }
    public double? RiskRating { get; set; }
    public string? Notes { get; set; }
    public DateTime? DeletedAt { get; set; }

    public virtual CountryDetails? Details { get; set; }
    public virtual ICollection<Company> Companies { get; set; } = [];
    public virtual ICollection<TradeBloc> TradeBlocs { get; set; } = [];
    public virtual ICollection<Event> Events { get; set; } = [];
    public virtual ICollection<CountryAdvantage> Advantages { get; set; } = [];
    public virtual ICollection<CountryChallenge> Challenges { get; set; } = [];
    public virtual ICollection<GdpSnapshot> GdpHistory { get; set; } = [];
}
