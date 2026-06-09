using simple_bloomberg_terminal.IoCore;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

/// <summary>The kind of perturbation an event applies to the economy (brief Section 3).</summary>
public enum ImpactKind
{
    /// <summary>Final-demand change in the origin sector → Leontief quantity (backward) cascade.</summary>
    Demand,

    /// <summary>Primary-input loss in the origin sector → Ghosh quantity (forward) cascade.</summary>
    Supply,

    /// <summary>Unit-cost rise in the origin sector → Leontief price (cost-push) cascade.</summary>
    Cost
}

/// <summary>A sector's data-derived orientation and the shock type it suggests in the UI.</summary>
public sealed record SectorProfile(int Index, string Sector, double FinalDemandShare, string Orientation, ImpactKind SuggestedKind);

/// <summary>A company's share of its sector's impact.</summary>
public sealed record CompanyImpact(long CompanyId, string Name, double? Revenue, double Weight, double Value);

/// <summary>One GICS industry within a sector: the companies in it and their summed impact.</summary>
public sealed record IndustryImpact(string Industry, double Value, IReadOnlyList<CompanyImpact> Companies);

/// <summary>One sector's response, with the companies that sit in it (flat + grouped by GICS industry).</summary>
public sealed record SectorImpact(
    int SectorIndex, string Sector, double Value,
    IReadOnlyList<CompanyImpact> Companies, IReadOnlyList<IndustryImpact> Industries);

/// <summary>One propagation round's contribution across all sectors (for the cascade-trace view).</summary>
public sealed record CascadeRound(int Round, double[] Contributions);

/// <summary>The full, explainable impact of an event.</summary>
public sealed record ImpactResult(
    ImpactKind Kind,
    int OriginSector,
    double Magnitude,
    bool IsPrice,
    double ConditionNumber,
    IReadOnlyList<SectorImpact> Sectors,
    IReadOnlyList<CascadeRound> Trace);

/// <summary>
/// Adapter between the domain (a UI request, later a real <c>Event</c>) and the pure I-O core. It
/// is the only component that knows both worlds: it turns "sector X is first hit, kind K, size M"
/// into a raw Δd / Δv / Δw, runs the matching solver, then maps the result vector back to sectors
/// and allocates each sector's impact to the companies that live in it. The core stays domain-blind.
/// </summary>
public sealed class EventImpactService(LoadedIoModel loaded, ICompanyRepository companies)
{
    private readonly IoModel _model = loaded.Model;
    private readonly IoSolver _solver = loaded.Solver;

    public ImpactResult Solve(ImpactKind kind, Sector origin, double magnitude)
    {
        var n = _model.N;
        var o = (int)origin;
        var shock = new double[n];
        shock[o] = magnitude;

        double[] response;
        var isPrice = false;
        ShockTrace? trace = null;

        switch (kind)
        {
            case ImpactKind.Demand:
                response = _solver.SolveDemand(shock);
                trace = IoShock.DemandCascade(_model.A, shock);
                break;
            case ImpactKind.Cost:
                response = _solver.SolvePrice(shock);
                trace = IoShock.PriceCascade(_model.A, shock);
                isPrice = true;
                break;
            case ImpactKind.Supply:
                response = _solver.SolveSupply(shock);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var bySector = companies.GetAll()
            .GroupBy(c => c.Sector)
            .ToDictionary(g => g.Key, g => g.ToList());

        var sectors = IoMetrics.RankMostAffected(response)
            .Select(r => BuildSectorImpact(r, bySector, isPrice))
            .ToList();

        var rounds = trace?.Rounds.Select(rd => new CascadeRound(rd.Round, rd.Contribution)).ToList()
                     ?? new List<CascadeRound>();

        return new ImpactResult(kind, o, magnitude, isPrice, _solver.ConditionNumber, sectors, rounds);
    }

    /// <summary>
    /// Per-sector orientation derived from the data: a sector's final-demand share (d/x) says how
    /// much of its output goes to end-users vs to other businesses. Near-zero share = an upstream
    /// supplier (extracted/intermediate) for which a SUPPLY outage is the natural lever; a high
    /// share = a final-demand-facing sector for which a DEMAND shock is natural. Used to suggest a
    /// shock type in the UI — guidance, not a restriction. Cost shocks apply to any sector.
    /// </summary>
    public IReadOnlyDictionary<Sector, SectorProfile> Profiles()
    {
        var x = _solver.BaselineOutput();
        var d = _model.D;
        var map = new Dictionary<Sector, SectorProfile>();
        foreach (var s in SectorIndex.All)
        {
            var i = (int)s;
            var share = x[i] != 0 ? d[i] / x[i] : 0;
            var (orientation, suggested) = share switch
            {
                < 0.35 => ("Upstream / supply-driven", ImpactKind.Supply),
                > 0.55 => ("Demand-facing", ImpactKind.Demand),
                _ => ("Mixed", ImpactKind.Demand)
            };
            map[s] = new SectorProfile(i, SectorIndex.Name(i), share, orientation, suggested);
        }
        return map;
    }

    private static SectorImpact BuildSectorImpact(
        RankedImpact ranked, IReadOnlyDictionary<Sector, List<Models.Entities.Company>> bySector, bool isPrice)
    {
        var sector = SectorIndex.At(ranked.SectorIndex);
        var members = bySector.TryGetValue(sector, out var list) ? list : [];

        // Exposure weight = revenue share within the sector; companies with no revenue split the
        // residual equally so they still appear (brief Section 8, simplest allocation).
        var withRevenue = members.Where(c => c.RevenueTotal is > 0).ToList();
        var totalRevenue = withRevenue.Sum(c => c.RevenueTotal!.Value);

        var withIndustry = members
            .Select(c =>
            {
                double weight;
                if (totalRevenue > 0)
                    weight = c.RevenueTotal is > 0 ? c.RevenueTotal!.Value / totalRevenue : 0;
                else
                    weight = 1.0 / members.Count;

                // Quantity shock: split the sector's Δ output by exposure weight.
                // Price shock: Δp is a fractional cost change, so the $ hit is revenue × Δp.
                var value = isPrice
                    ? (c.RevenueTotal ?? 0) * ranked.Value
                    : ranked.Value * weight;

                return (c.Industry, Impact: new CompanyImpact(c.Id, c.Name, c.RevenueTotal, weight, value));
            })
            .ToList();

        var companyImpacts = withIndustry
            .Select(x => x.Impact)
            .OrderByDescending(ci => Math.Abs(ci.Value))
            .ToList();

        // Sub-group the sector's companies by GICS industry (the finer "who's hit" view). Each
        // group's Δ is just the sum of its members' Δ — the matrix math is untouched. Companies
        // with no Industry fall under "Unclassified".
        var industries = withIndustry
            .GroupBy(x => x.Industry)
            .Select(g => new IndustryImpact(
                g.Key?.ToString().Replace('_', ' ') ?? "Unclassified",
                g.Sum(x => x.Impact.Value),
                g.Select(x => x.Impact).OrderByDescending(ci => Math.Abs(ci.Value)).ToList()))
            .OrderByDescending(ig => Math.Abs(ig.Value))
            .ToList();

        return new SectorImpact(
            ranked.SectorIndex, SectorIndex.Name(ranked.SectorIndex), ranked.Value, companyImpacts, industries);
    }
}
