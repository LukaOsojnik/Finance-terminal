namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// A primary (value-added) input whose unit cost a factor-broadcast shock raises across all sectors.
/// Maps 1:1 to the value-added vectors in <c>IoModel</c>. CAPITAL = cost of financing → the rate-hike
/// lever. ENERGY is endogenous in the model (its vector is all-zero), so a factor-energy broadcast
/// does nothing — model an energy price spike as a SECTOR cost shock on ENERGY instead.
/// </summary>
public enum CostFactor
{
    LABOR,
    ENERGY,
    CAPITAL,
    TAXES
}
