using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

public class CountryAdvantage
{
    [Key]
    public long Id { get; set; }
    public long CountryId { get; set; }
    public string Text { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CountryId")]
    public virtual Country? Country { get; set; }
}
