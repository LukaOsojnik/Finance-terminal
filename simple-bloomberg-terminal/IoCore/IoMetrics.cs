namespace simple_bloomberg_terminal.IoCore;

/// <summary>A sector's signed response, ranked by absolute magnitude.</summary>
public sealed record RankedImpact(int SectorIndex, double Value);

/// <summary>
/// Sector-level summary statistics derived from the Leontief inverse L and a solved response
/// vector (brief Section 7). All inputs/outputs are plain arrays indexed by <see cref="SectorIndex"/>.
/// </summary>
public static class IoMetrics
{
    /// <summary>
    /// Output multiplier of each sector = column sum of L. It is the total gross output, direct and
    /// indirect, generated economy-wide per unit of final demand for that sector. Every multiplier
    /// is ≥ 1 in a valid model (the sector's own unit plus everything it pulls upstream).
    /// </summary>
    public static double[] OutputMultipliers(double[,] l)
    {
        var n = l.GetLength(0);
        var m = new double[n];
        for (var j = 0; j < n; j++)
        {
            var sum = 0.0;
            for (var i = 0; i < n; i++) sum += l[i, j];
            m[j] = sum;
        }
        return m;
    }

    /// <summary>
    /// Backward-linkage centrality = row sum of L, normalised by the matrix mean. A sector scoring
    /// &gt; 1 is more downstream-dependent than average (it draws heavily, directly and indirectly,
    /// on the rest of the economy). Cheap structural proxy that needs no eigen-solve.
    /// </summary>
    public static double[] Centrality(double[,] l)
    {
        var n = l.GetLength(0);
        var rowSums = new double[n];
        var grand = 0.0;
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < n; j++) sum += l[i, j];
            rowSums[i] = sum;
            grand += sum;
        }

        var mean = grand / n;
        var c = new double[n];
        for (var i = 0; i < n; i++) c[i] = mean == 0 ? 0 : rowSums[i] / mean;
        return c;
    }

    /// <summary>
    /// Sectors ordered by absolute response, largest first — the "most affected" list. Sectors with
    /// a negligible response (below <paramref name="threshold"/>) are dropped.
    /// </summary>
    public static IReadOnlyList<RankedImpact> RankMostAffected(double[] response, double threshold = 1e-9)
        => response
            .Select((value, index) => new RankedImpact(index, value))
            .Where(r => Math.Abs(r.Value) > threshold)
            .OrderByDescending(r => Math.Abs(r.Value))
            .ToList();
}
