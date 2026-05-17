using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class EventRowViewModel
{
    public required Event Event { get; set; }
    public required string ImpactClass { get; set; }
    public required string TypeLabel { get; set; }
    public string StatusLabel { get; set; } = "";

    public static EventRowViewModel From(Event ev) => new()
    {
        Event = ev,
        TypeLabel = ev.Type.ToString().Replace("_", " "),
        ImpactClass = ev.ImpactScore.HasValue
            ? (ev.ImpactScore.Value >= 7 ? "impact-high"
                : ev.ImpactScore.Value >= 4 ? "impact-medium"
                : "impact-low")
            : ""
    };
}
