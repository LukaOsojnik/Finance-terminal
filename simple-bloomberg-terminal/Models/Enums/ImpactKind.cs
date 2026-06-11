namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>The kind of perturbation an event applies to the economy (impact brief Section 3).</summary>
public enum ImpactKind
{
    /// <summary>Final-demand change in the origin sector → Leontief quantity (backward) cascade.</summary>
    Demand,

    /// <summary>Primary-input loss in the origin sector → Ghosh quantity (forward) cascade.</summary>
    Supply,

    /// <summary>Unit-cost rise in the origin sector → Leontief price (cost-push) cascade.</summary>
    Cost
}
