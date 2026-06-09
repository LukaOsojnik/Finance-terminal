using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.IoCore;

/// <summary>
/// The single source of truth mapping the canonical <see cref="Sector"/> enum to matrix indices.
/// Index == enum ordinal, so the 11 GICS sectors define the fixed row/column ordering used by every
/// matrix and vector in the I-O core (brief Section 1 / Section 2.1). Do not reorder the enum.
/// </summary>
public static class SectorIndex
{
    /// <summary>Sectors in canonical (declaration) order; position i is matrix index i.</summary>
    public static readonly Sector[] All = Enum.GetValues<Sector>();

    /// <summary>n — the number of sectors (11 for phase 1).</summary>
    public static readonly int Count = All.Length;

    /// <summary>Matrix index of a sector (its enum ordinal).</summary>
    public static int IndexOf(Sector sector) => (int)sector;

    /// <summary>The sector occupying a given matrix index.</summary>
    public static Sector At(int index) => All[index];

    /// <summary>Human-readable label, e.g. "CONSUMER DISCRETIONARY".</summary>
    public static string Name(int index) => All[index].ToString().Replace('_', ' ');
}
