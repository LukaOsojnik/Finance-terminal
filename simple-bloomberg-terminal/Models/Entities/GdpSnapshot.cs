using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace simple_bloomberg_terminal.Models.Entities;

public class GdpSnapshot
{
    [Key]
    public long Id { get; set; }
    public long CountryId { get; set; }
    public int Year { get; set; }
    public double GdpUsd { get; set; }

    [ForeignKey("CountryId")]
    public virtual Country? Country { get; set; }
}
