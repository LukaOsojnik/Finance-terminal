using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

public class CountryDetails
{
    [Key]
    public long CountryId { get; set; }
    public string MarketPosition { get; set; } = string.Empty;
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CountryId")]
    public virtual Country? Country { get; set; }
    public virtual ICollection<CountryAdvantage> Advantages { get; set; } = [];
    public virtual ICollection<CountryChallenge> Challenges { get; set; } = [];
    public virtual ICollection<GdpSnapshot> GdpHistory { get; set; } = [];
}
