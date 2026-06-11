using System.ComponentModel.DataAnnotations;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A named, reusable bundle of economic shocks the user composes in the builder and replays on the
/// Impact page (e.g. "Rate hike", "Oil shock — war"). Because the I-O model is linear, a scenario is
/// just the superposition of its <see cref="Shocks"/>: the engine sums same-family shock vectors and
/// solves once per family. Stored as data so users create scenarios without touching code.
/// </summary>
public class Scenario
{
    [Key]
    public long Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime? DeletedAt { get; set; }

    public virtual ICollection<ScenarioShock> Shocks { get; set; } = new List<ScenarioShock>();
}
