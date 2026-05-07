using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryDetailsViewModel
{
    public required Country Country { get; set; }

    public string? MarketPosition { get; set; }

    public ICollection<CountryAdvantage> Advantages { get; set; } = [];

    public ICollection<CountryChallenge> Challenges { get; set; } = [];

    public List<Company> TopCompanies { get; set; } = [];

    public List<TradeBloc> TradeBlocs { get; set; } = [];

    public ICollection<GdpSnapshot> GdpHistory { get; set; } = [];
}
