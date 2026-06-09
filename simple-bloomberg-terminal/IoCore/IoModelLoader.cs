using System.Text.Json;
using System.Text.Json.Serialization;

namespace simple_bloomberg_terminal.IoCore;

/// <summary>Thrown when a loaded I-O model violates an economic or numerical invariant.</summary>
public sealed class IoModelValidationException(string message) : Exception(message);

/// <summary>The validated model paired with the solver built from it (reused as the app singleton).</summary>
public sealed record LoadedIoModel(IoModel Model, IoSolver Solver);

/// <summary>
/// Loads an I-O model from a versioned JSON artifact and enforces every Section-6 invariant before
/// the model is allowed into the application. Validation throws — the brief's "fail loudly" rule:
/// a model that silently violates Hawkins–Simon would produce nonsense rankings.
/// </summary>
public static class IoModelLoader
{
    // Numerical slack for the equality-style invariants (baseline price = 1, linearity).
    private const double Tolerance = 1e-6;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static LoadedIoModel LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new IoModelValidationException($"I-O model file not found: {path}");

        var dto = JsonSerializer.Deserialize<IoModelDto>(File.ReadAllText(path), JsonOptions)
                  ?? throw new IoModelValidationException($"I-O model file is empty or malformed: {path}");

        var model = dto.ToModel();
        Validate(model);                 // economic invariants on A
        var solver = new IoSolver(model);
        ValidateSolver(model, solver);   // numerical invariants on L and the solves
        return new LoadedIoModel(model, solver);
    }

    private static void Validate(IoModel m)
    {
        if (m.N != SectorIndex.Count)
            throw new IoModelValidationException(
                $"Model has {m.N} sectors but the canonical Sector enum has {SectorIndex.Count}.");

        // Non-negativity of A (brief Section 6).
        for (var i = 0; i < m.N; i++)
            for (var j = 0; j < m.N; j++)
                if (m.A[i, j] < 0)
                    throw new IoModelValidationException(
                        $"A[{i},{j}] = {m.A[i, j]} is negative; technical coefficients must be ≥ 0.");

        // Productivity / Hawkins–Simon: each column sum of A < 1 (value added is the remainder).
        for (var j = 0; j < m.N; j++)
        {
            var colSum = 0.0;
            for (var i = 0; i < m.N; i++) colSum += m.A[i, j];
            if (colSum >= 1.0)
                throw new IoModelValidationException(
                    $"Column sum of A for sector {SectorIndex.Name(j)} = {colSum:F4} ≥ 1; " +
                    "violates Hawkins–Simon (intermediate-input share must be < 1).");
        }

        // Spectral radius ρ(A) < 1 guarantees L = ΣA^k converges and L ≥ 0.
        var rho = IoSolver.SpectralRadius(m.A);
        if (rho >= 1.0)
            throw new IoModelValidationException($"Spectral radius ρ(A) = {rho:F4} ≥ 1; L would not converge.");

        // Value-added components must be non-negative so factor shocks have a sane sign.
        for (var j = 0; j < m.N; j++)
            if (m.Labor[j] < 0 || m.Energy[j] < 0 || m.Capital[j] < 0 || m.Taxes[j] < 0)
                throw new IoModelValidationException(
                    $"A value-added component for sector {SectorIndex.Name(j)} is negative.");
    }

    private static void ValidateSolver(IoModel m, IoSolver solver)
    {
        var l = solver.LeontiefInverse();

        // L ≥ 0 everywhere; diagonal ≥ 1 (a sector needs at least its own unit of output).
        for (var i = 0; i < m.N; i++)
        {
            for (var j = 0; j < m.N; j++)
                if (l[i, j] < -Tolerance)
                    throw new IoModelValidationException($"L[{i},{j}] = {l[i, j]} is negative.");
            if (l[i, i] < 1.0 - Tolerance)
                throw new IoModelValidationException(
                    $"L diagonal for {SectorIndex.Name(i)} = {l[i, i]:F4} < 1.");
        }

        // Every output multiplier (column sum of L) ≥ 1.
        foreach (var (mult, j) in IoMetrics.OutputMultipliers(l).Select((v, j) => (v, j)))
            if (mult < 1.0 - Tolerance)
                throw new IoModelValidationException(
                    $"Output multiplier for {SectorIndex.Name(j)} = {mult:F4} < 1.");

        // Baseline price fixture: with v = (I-A)^T·1 the price solve must return p = 1 everywhere.
        foreach (var (p, j) in solver.BaselinePrices().Select((p, j) => (p, j)))
            if (Math.Abs(p - 1.0) > Tolerance)
                throw new IoModelValidationException(
                    $"Baseline price for {SectorIndex.Name(j)} = {p:F6} ≠ 1 (price-model regression fixture).");

        // Zero shock ⇒ zero response.
        if (solver.SolveDemand(new double[m.N]).Any(x => Math.Abs(x) > Tolerance))
            throw new IoModelValidationException("Zero demand shock produced a non-zero response.");

        // Linearity: Δx(2·Δd) == 2·Δx(Δd).
        var probe = new double[m.N];
        probe[0] = 1.0;
        var single = solver.SolveDemand(probe);
        var doubled = solver.SolveDemand(probe.Select(x => 2 * x).ToArray());
        for (var i = 0; i < m.N; i++)
            if (Math.Abs(doubled[i] - 2 * single[i]) > Tolerance)
                throw new IoModelValidationException("Demand solve is not linear (scaling test failed).");
    }

    /// <summary>JSON shape of a versioned I-O model artifact.</summary>
    private sealed class IoModelDto
    {
        public int Version { get; init; }
        public string? Source { get; init; }

        /// <summary>Sector names in matrix order; must match the canonical Sector enum exactly.</summary>
        [JsonPropertyName("sectorOrder")]
        public List<string> SectorOrder { get; init; } = [];

        /// <summary>Technical-coefficients matrix as rows of columns: a[i][j].</summary>
        public List<List<double>> A { get; init; } = [];
        public List<double> Labor { get; init; } = [];
        public List<double> Energy { get; init; } = [];
        public List<double> Capital { get; init; } = [];
        public List<double> Taxes { get; init; } = [];
        public List<double> D { get; init; } = [];

        public IoModel ToModel()
        {
            var expected = SectorIndex.All.Select(s => s.ToString()).ToList();
            if (!SectorOrder.SequenceEqual(expected))
                throw new IoModelValidationException(
                    "sectorOrder in the JSON does not match the canonical Sector enum order.");

            var n = SectorOrder.Count;
            if (A.Count != n || A.Any(row => row.Count != n))
                throw new IoModelValidationException($"A must be {n}x{n}.");

            var a = new double[n, n];
            for (var i = 0; i < n; i++)
                for (var j = 0; j < n; j++)
                    a[i, j] = A[i][j];

            return new IoModel(a, Labor.ToArray(), Energy.ToArray(), Capital.ToArray(),
                Taxes.ToArray(), D.ToArray());
        }
    }
}
