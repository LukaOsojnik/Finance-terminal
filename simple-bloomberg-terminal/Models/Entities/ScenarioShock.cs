using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// One shock component of a <see cref="Scenario"/>: a single perturbation applied to the economy.
/// <see cref="Target"/> picks whether it hits one <see cref="Sector"/> (the classic single-sector
/// levers) or broadcasts across all sectors via one <see cref="Factor"/> (e.g. a rate hike = a
/// CAPITAL-factor cost push). Exactly one of <see cref="Sector"/>/<see cref="Factor"/> is set, per
/// <see cref="Target"/>. <see cref="Kind"/> is forced to <see cref="ImpactKind.Cost"/> for FACTOR
/// shocks (a factor is a value-added input, so it can only drive the price cascade).
/// </summary>
public class ScenarioShock
{
    [Key]
    public long Id { get; set; }

    public long ScenarioId { get; set; }

    public ImpactKind Kind { get; set; }

    public ShockTarget Target { get; set; }

    /// <summary>Set when <see cref="Target"/> is SECTOR; the GICS sector the shock originates in.</summary>
    public Sector? Sector { get; set; }

    /// <summary>Set when <see cref="Target"/> is FACTOR; the value-added input bumped economy-wide.</summary>
    public CostFactor? Factor { get; set; }

    /// <summary>
    /// Shock size. Demand/Supply = absolute $ (BEA scale, $M; negative = collapse/outage). Cost =
    /// a fraction (0.10 = +10% unit cost). For a FACTOR shock it scales that factor's intensity vector.
    /// </summary>
    public double Magnitude { get; set; }

    public DateTime? DeletedAt { get; set; }

    [ForeignKey(nameof(ScenarioId))]
    public virtual Scenario? Scenario { get; set; }
}
