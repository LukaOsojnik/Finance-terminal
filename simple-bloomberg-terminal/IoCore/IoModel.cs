namespace simple_bloomberg_terminal.IoCore;

/// <summary>
/// The static, linear input-output model: the technical-coefficients matrix A, the value-added
/// intensities split into the three primary inputs (brief Section 2.3, kept separable so
/// factor-specific shocks remain possible), and the baseline final demand d.
///
/// Pure data — no linear-algebra library and no domain (EF / event) types. All arrays are indexed
/// by <see cref="SectorIndex"/>. A[i,j] = units of sector i required to produce one unit of sector
/// j's output (column j is the input recipe for sector j).
/// </summary>
public sealed class IoModel
{
    public int N { get; }

    /// <summary>Technical-coefficients matrix. A[i,j] = direct input of i per unit output of j.</summary>
    public double[,] A { get; }

    /// <summary>Labor cost per unit output (the "people" primary input).</summary>
    public double[] Labor { get; }

    /// <summary>Energy cost per unit output (the exogenous "gas/grid" primary input).</summary>
    public double[] Energy { get; }

    /// <summary>Capital / financing cost per unit output (the "money/banks" primary input).</summary>
    public double[] Capital { get; }

    /// <summary>Taxes on production per unit output.</summary>
    public double[] Taxes { get; }

    /// <summary>Baseline final demand per sector; L·d reproduces benchmark gross output x.</summary>
    public double[] D { get; }

    public IoModel(double[,] a, double[] labor, double[] energy, double[] capital, double[] taxes, double[] d)
    {
        N = labor.Length;
        if (a.GetLength(0) != N || a.GetLength(1) != N)
            throw new ArgumentException($"A must be {N}x{N}.", nameof(a));
        if (energy.Length != N || capital.Length != N || taxes.Length != N || d.Length != N)
            throw new ArgumentException("All vectors must have length N.");

        A = a;
        Labor = labor;
        Energy = energy;
        Capital = capital;
        Taxes = taxes;
        D = d;
    }

    /// <summary>
    /// Value added per unit output, v[j] = labor + energy + capital + taxes. This is the vector the
    /// cost-push (Leontief price) model consumes; a factor-specific shock perturbs one component
    /// before re-summing here.
    /// </summary>
    public double[] V()
    {
        var v = new double[N];
        for (var j = 0; j < N; j++)
            v[j] = Labor[j] + Energy[j] + Capital[j] + Taxes[j];
        return v;
    }
}
