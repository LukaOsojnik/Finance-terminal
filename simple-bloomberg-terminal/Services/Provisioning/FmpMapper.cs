using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Provisioning;

/// <summary>
/// Pure mapping from FMP profile/income payloads onto a <see cref="CompanyCreateModel"/>.
/// Sector is GICS-flavored but not our exact enum names ("Technology" != INFORMATION_TECHNOLOGY), so
/// it's matched through a normalized lookup, falling back to a safe default. The industry tier is left
/// to the LLM: the raw FMP label is carried on <c>FmpIndustry</c> and resolved to a GICS sub-industry
/// (then rolled up) by <see cref="IIndustryClassifier"/> — no hand-maintained label dictionary here.
/// </summary>
public static class FmpMapper
{
    public static CompanyCreateModel ToCreateModel(FmpProfile p, FmpIncome? income)
    {
        var model = new CompanyCreateModel
        {
            Name = p.CompanyName ?? p.Symbol ?? "",
            Cik = NormalizeCik(p.Cik),
            // Store the vendor label verbatim; the GICS sub-industry/industry are resolved from it later.
            FmpIndustry = string.IsNullOrWhiteSpace(p.Industry) ? null : p.Industry.Trim(),
            // Unknown FMP sector -> null (unclassified), not a guess. The industry classifier's
            // unconstrained fallback fills the sector from the chosen sub-industry.
            Sector = MapSector(p.Sector),
            MarketCap = p.MarketCap,
            Notes = Truncate(p.Description, 2000)
        };

        if (income != null)
        {
            // Stable income has no ratio fields, so derive margin = grossProfit / revenue (0–1).
            // Round to 2 dp to satisfy the form's step="0.01" validation.
            if (income.Revenue is { } rev && rev != 0 && income.GrossProfit is { } gp)
                model.GrossMargin = Math.Round(gp / rev, 2);
            if (DateOnly.TryParse(income.Date, out var d))
                model.AsOf = d;
            // RevenueTotal is a plain USD double — only fill it when FMP reported in USD.
            if (string.Equals(income.ReportedCurrency, "USD", StringComparison.OrdinalIgnoreCase))
                model.RevenueTotal = income.Revenue;
        }

        return model;
    }

    public static Sector? MapSector(string? fmpSector) =>
        fmpSector != null && SectorMap.TryGetValue(Normalize(fmpSector), out var s) ? s : null;

    // Strip everything but letters/digits and lowercase, so "Software—Infrastructure",
    // "Software - Application" and "Financial Services" all reduce to a stable lookup key.
    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? NormalizeCik(string? cik) => Cik.Normalize(cik);

    private static string? Truncate(string? s, int max) =>
        string.IsNullOrEmpty(s) ? s : s.Length <= max ? s : s[..max];

    // FMP's 11 sector labels -> our GICS Sector enum.
    private static readonly Dictionary<string, Sector> SectorMap = new()
    {
        ["technology"] = Sector.INFORMATION_TECHNOLOGY,
        ["financialservices"] = Sector.FINANCIALS,
        ["financial"] = Sector.FINANCIALS,
        ["healthcare"] = Sector.HEALTH_CARE,
        ["consumercyclical"] = Sector.CONSUMER_DISCRETIONARY,
        ["consumerdefensive"] = Sector.CONSUMER_STAPLES,
        ["energy"] = Sector.ENERGY,
        ["basicmaterials"] = Sector.MATERIALS,
        ["industrials"] = Sector.INDUSTRIALS,
        ["communicationservices"] = Sector.COMMUNICATION_SERVICES,
        ["utilities"] = Sector.UTILITIES,
        ["realestate"] = Sector.REAL_ESTATE
    };
}
