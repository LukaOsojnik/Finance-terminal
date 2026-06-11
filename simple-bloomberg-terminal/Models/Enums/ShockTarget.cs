namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// What a scenario shock component points at. A <see cref="SECTOR"/> shock perturbs one GICS sector
/// (the three classic single-sector levers). A <see cref="FACTOR"/> shock is economy-wide: it bumps
/// one value-added factor (e.g. capital cost = a rate hike) across every sector at once, weighted by
/// that sector's factor intensity. Factor shocks are always cost-push (a value-added perturbation).
/// </summary>
public enum ShockTarget
{
    SECTOR,
    FACTOR
}
