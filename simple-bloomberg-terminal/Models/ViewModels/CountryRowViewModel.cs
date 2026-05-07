using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class CountryRowViewModel
{
    public required Country Country { get; set; }
    public required string RiskClass { get; set; }

    public static CountryRowViewModel From(Country c) => new()
    {
        Country = c,
        RiskClass = c.RiskRating.HasValue
            ? (c.RiskRating.Value <= 3.0 ? "risk-low"
                : c.RiskRating.Value <= 6.0 ? "risk-medium"
                : "risk-high")
            : "muted-cell"
    };
}
