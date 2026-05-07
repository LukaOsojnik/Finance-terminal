using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.Entities;

public class TradeBloc
{
    public TradeBloc(string name, string code)
    {
        Name = name;
        Code = code;
    }

    [Key]
    public long Id { get; set; }
    public string Name { get; set; }
    public string Code { get; set; }
    public string? Description { get; set; }
    public DateOnly? FoundedDate { get; set; }

    public virtual ICollection<Country> Countries { get; set; } = [];
    public virtual ICollection<Event> Events { get; set; } = [];
}
