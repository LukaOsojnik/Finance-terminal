namespace simple_bloomberg_terminal.IoCore;

/// <summary>
/// One propagation round's contribution: which sectors moved at "distance" <see cref="Round"/>
/// from the originating shock (round 0 = the direct shock itself, round 1 = first-order suppliers,
/// round 2 = their suppliers, …).
/// </summary>
public sealed record ShockRound(int Round, double[] Contribution);

/// <summary>
/// The full cascade result: the converged total response and the per-round contributions that
/// sum to it. Keeping the rounds is the brief's traceability guardrail (Section 7) — the total
/// alone is a black box; the rounds let the UI explain *why* each sector was hit and *how far*
/// down the chain it sits.
/// </summary>
public sealed record ShockTrace(double[] Total, IReadOnlyList<ShockRound> Rounds, bool Converged)
{
    /// <summary>Number of propagation rounds actually computed (excluding the round-0 direct shock).</summary>
    public int Depth => Rounds.Count - 1;
}

/// <summary>
/// Decomposes a linear shock into its Neumann-series rounds. The Leontief total response is
/// Δx = L·Δd = (I + A + A² + …)·Δd; this walks that series term by term so the propagation path
/// is recoverable. ρ(A) &lt; 1 (enforced by the loader) guarantees convergence.
///
/// The same machinery serves the cost-push dual by walking A^T instead of A
/// (Δp^T = Δv^T(I + A + A² + …) ⇒ round k is (A^T)^k·Δv).
/// </summary>
public static class IoShock
{
    private const double DefaultTolerance = 1e-9;
    private const int DefaultMaxRounds = 100;

    /// <summary>Demand-pull cascade Δx = Σ A^k·Δd, decomposed by round.</summary>
    public static ShockTrace DemandCascade(double[,] a, double[] deltaD,
        double tolerance = DefaultTolerance, int maxRounds = DefaultMaxRounds)
        => Cascade(a, deltaD, transpose: false, tolerance, maxRounds);

    /// <summary>Cost-push cascade Δp = Σ (A^T)^k·Δv, decomposed by round.</summary>
    public static ShockTrace PriceCascade(double[,] a, double[] deltaV,
        double tolerance = DefaultTolerance, int maxRounds = DefaultMaxRounds)
        => Cascade(a, deltaV, transpose: true, tolerance, maxRounds);

    private static ShockTrace Cascade(double[,] a, double[] shock, bool transpose, double tolerance, int maxRounds)
    {
        var n = shock.Length;
        var total = (double[])shock.Clone();
        var rounds = new List<ShockRound> { new(0, (double[])shock.Clone()) };

        var current = shock;
        var converged = false;
        for (var k = 1; k <= maxRounds; k++)
        {
            var next = transpose ? MultiplyTranspose(a, current) : Multiply(a, current);
            for (var i = 0; i < n; i++) total[i] += next[i];
            rounds.Add(new ShockRound(k, next));

            if (MaxAbs(next) < tolerance)
            {
                converged = true;
                break;
            }
            current = next;
        }

        return new ShockTrace(total, rounds, converged);
    }

    // y = A·x
    private static double[] Multiply(double[,] a, double[] x)
    {
        var n = x.Length;
        var y = new double[n];
        for (var i = 0; i < n; i++)
        {
            var sum = 0.0;
            for (var j = 0; j < n; j++) sum += a[i, j] * x[j];
            y[i] = sum;
        }
        return y;
    }

    // y = A^T·x
    private static double[] MultiplyTranspose(double[,] a, double[] x)
    {
        var n = x.Length;
        var y = new double[n];
        for (var j = 0; j < n; j++)
        {
            var sum = 0.0;
            for (var i = 0; i < n; i++) sum += a[i, j] * x[i];
            y[j] = sum;
        }
        return y;
    }

    private static double MaxAbs(double[] v)
    {
        var m = 0.0;
        foreach (var x in v) m = Math.Max(m, Math.Abs(x));
        return m;
    }
}
