using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class Event
{
    public Event(string title, EventType type, DateOnly date)
    {
        Title = title;
        Type = type;
        Date = date;
    }

    [Key]
    public long Id { get; set; }
    public string Title { get; set; }
    public EventType Type { get; set; }
    public DateOnly Date { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? Description { get; set; }
    public double? ImpactScore { get; set; }

    public virtual ICollection<Country> Countries { get; set; } = [];
    public virtual ICollection<Company> Companies { get; set; } = [];
    public virtual ICollection<TradeBloc> TradeBlocs { get; set; } = [];
}
