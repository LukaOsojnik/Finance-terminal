using MathNet.Numerics.LinearAlgebra;
using MathNet.Numerics.LinearAlgebra.Double;

namespace simple_bloomberg_terminal.IoCore;

/// <summary>
/// The three linear solvers of the I-O core, all built on the Leontief inverse L = (I-A)^-1
/// (brief Section 1). L is obtained by an LU factorisation of (I-A) — we solve linear systems
/// rather than forming an explicit inverse (Section 5/7). Quantity and price are two independent
/// solves that share L; they are never coupled into a fixed point (no CGE).
///
/// Math.NET Numerics is confined to this class; everything else in the core trades in double[].
/// </summary>
public sealed class IoSolver
{
    private readonly int _n;
    private readonly Matrix<double> _a;
    private readonly Matrix<double> _l;   // Leontief inverse (I-A)^-1
    private readonly Matrix<double> _lt;  // L transpose, for the price dual
    private readonly Matrix<double> _g;   // Ghosh inverse (I-B)^-1
    private readonly Vector<double> _x;   // baseline gross output x = L d

    public IoSolver(IoModel model)
    {
        _n = model.N;
        _a = DenseMatrix.OfArray(model.A);
        var identity = DenseMatrix.CreateIdentity(_n);

        // L = (I-A)^-1 via LU factorisation of (I-A), reused across the identity columns.
        var iMinusA = identity - _a;
        ConditionNumber = iMinusA.ConditionNumber();
        var lu = iMinusA.LU();
        _l = lu.Solve(identity);
        _lt = _l.Transpose();

        // Baseline output x = L d, then the Ghosh allocation matrix B[i,j] = A[i,j]·x[j]/x[i],
        // and G = (I-B)^-1 for the supply-push (forward) solve.
        _x = _l * DenseVector.OfArray(model.D);
        _g = BuildGhoshInverse(_a, _x);
    }

    /// <summary>Condition number of (I-A); large values mean the solve is ill-conditioned.</summary>
    public double ConditionNumber { get; }

    /// <summary>Leontief inverse L (total-requirements matrix) as a plain array.</summary>
    public double[,] LeontiefInverse() => _l.ToArray();

    /// <summary>Baseline gross output x = L d.</summary>
    public double[] BaselineOutput() => _x.ToArray();

    /// <summary>Spectral radius ρ(A) — must be &lt; 1 for L = ΣA^k to converge (Hawkins–Simon).</summary>
    public static double SpectralRadius(double[,] a)
    {
        var evd = DenseMatrix.OfArray(a).Evd();
        return evd.EigenValues.Enumerate().Max(c => c.Magnitude);
    }

    /// <summary>
    /// Demand-pull (Leontief quantity, backward): Δx = L·Δd. A change in final demand for some
    /// sectors propagates to every sector's gross output.
    /// </summary>
    public double[] SolveDemand(double[] deltaD) => (_l * DenseVector.OfArray(deltaD)).ToArray();

    /// <summary>
    /// Cost-push (Leontief price, the dual): Δp^T = Δv^T·L, i.e. Δp = L^T·Δv. A rise in a value-added
    /// component (e.g. energy) propagates into every sector's unit cost.
    /// </summary>
    public double[] SolvePrice(double[] deltaV) => (_lt * DenseVector.OfArray(deltaV)).ToArray();

    /// <summary>
    /// Cost-push baseline fixture: with v = (I-A)^T·1 the price solve must return p = 1 everywhere
    /// (brief Section 6). Returned so the loader can assert it.
    /// </summary>
    public double[] BaselinePrices()
    {
        var ones = DenseVector.Create(_n, 1.0);
        var v = (DenseMatrix.CreateIdentity(_n) - _a).Transpose() * ones;
        return _lt.Multiply(v).ToArray();
    }

    /// <summary>
    /// Supply-push (Ghosh quantity, forward): Δx^T = Δw^T·G, i.e. Δx = G^T·Δw. A loss of a primary
    /// input to some sectors propagates downstream to their customers' output.
    /// </summary>
    public double[] SolveSupply(double[] deltaW) => (_g.Transpose() * DenseVector.OfArray(deltaW)).ToArray();

    private static Matrix<double> BuildGhoshInverse(Matrix<double> a, Vector<double> x)
    {
        var n = a.RowCount;
        var b = DenseMatrix.Create(n, n, 0.0);
        for (var i = 0; i < n; i++)
        {
            // x[i] == 0 would mean sector i produces nothing, so it allocates nothing downstream.
            if (x[i] == 0) continue;
            for (var j = 0; j < n; j++)
                b[i, j] = a[i, j] * x[j] / x[i];
        }

        var identity = DenseMatrix.CreateIdentity(n);
        return (identity - b).LU().Solve(identity);
    }
}
