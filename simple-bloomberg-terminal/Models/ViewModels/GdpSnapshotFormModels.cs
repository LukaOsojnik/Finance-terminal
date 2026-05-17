using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class GdpSnapshotCreateModel
{
    [Required]
    public long CountryId { get; set; }

    [Required, Range(1900, 2200)]
    public int Year { get; set; }

    [Required]
    public double GdpUsd { get; set; }
}

public class GdpSnapshotEditModel : GdpSnapshotCreateModel
{
    public long Id { get; set; }
}
