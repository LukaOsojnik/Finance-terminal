using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Pure mapping from FMP profile/income payloads onto a <see cref="CompanyCreateModel"/>.
/// The hard part is taxonomy: FMP's free-form sector/industry labels are GICS-flavored but
/// not our exact enum names ("Technology" != INFORMATION_TECHNOLOGY, "Consumer Electronics"
/// != any enum), so each is matched through a normalized lookup. Unmatched industry -> null
/// (the user picks it on review); sector falls back to the FMP sector, then a safe default.
/// </summary>
public static class FmpMapper
{
    public static CompanyCreateModel ToCreateModel(FmpProfile p, FmpIncome? income)
    {
        var industry = MapIndustry(p.Industry);
        var model = new CompanyCreateModel
        {
            Name = p.CompanyName ?? p.Symbol ?? "",
            Cik = NormalizeCik(p.Cik),
            Industry = industry,
            // Prefer the sector implied by the (more specific) industry; fall back to FMP's sector.
            Sector = industry?.GetSector() ?? MapSector(p.Sector) ?? Sector.INFORMATION_TECHNOLOGY,
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

    public static GicsIndustry? MapIndustry(string? fmpIndustry) =>
        fmpIndustry != null && IndustryMap.TryGetValue(Normalize(fmpIndustry), out var i) ? i : null;

    // Strip everything but letters/digits and lowercase, so "Software—Infrastructure",
    // "Software - Application" and "Financial Services" all reduce to a stable lookup key.
    private static string Normalize(string s) =>
        new string(s.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static string? NormalizeCik(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik)) return null;
        var digits = new string(cik.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits.PadLeft(10, '0');
    }

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

    // FMP/Yahoo industry labels -> our GICS Industry enum. Best-effort coverage of the common
    // labels; anything not here returns null and the user assigns it on the review screen.
    private static readonly Dictionary<string, GicsIndustry> IndustryMap = new()
    {
        // Information Technology
        ["consumerelectronics"] = GicsIndustry.TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS,
        ["computerhardware"] = GicsIndustry.TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS,
        ["softwareinfrastructure"] = GicsIndustry.SOFTWARE,
        ["softwareapplication"] = GicsIndustry.SOFTWARE,
        ["software"] = GicsIndustry.SOFTWARE,
        ["semiconductors"] = GicsIndustry.SEMICONDUCTORS_AND_SEMICONDUCTOR_EQUIPMENT,
        ["semiconductorequipmentmaterials"] = GicsIndustry.SEMICONDUCTORS_AND_SEMICONDUCTOR_EQUIPMENT,
        ["informationtechnologyservices"] = GicsIndustry.IT_SERVICES,
        ["communicationequipment"] = GicsIndustry.COMMUNICATIONS_EQUIPMENT,
        ["electroniccomponents"] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,
        ["scientifictechnicalinstruments"] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,

        // Communication Services
        ["internetcontentinformation"] = GicsIndustry.INTERACTIVE_MEDIA_AND_SERVICES,
        ["entertainment"] = GicsIndustry.ENTERTAINMENT,
        ["electronicgamingmultimedia"] = GicsIndustry.ENTERTAINMENT,
        ["telecomservices"] = GicsIndustry.DIVERSIFIED_TELECOMMUNICATION_SERVICES,
        ["advertisingagencies"] = GicsIndustry.MEDIA,
        ["broadcasting"] = GicsIndustry.MEDIA,
        ["publishing"] = GicsIndustry.MEDIA,

        // Financials
        ["banksdiversified"] = GicsIndustry.BANKS,
        ["banksregional"] = GicsIndustry.BANKS,
        ["banks"] = GicsIndustry.BANKS,
        ["insurancelife"] = GicsIndustry.INSURANCE,
        ["insurancepropertycasualty"] = GicsIndustry.INSURANCE,
        ["insurancediversified"] = GicsIndustry.INSURANCE,
        ["insurancebrokers"] = GicsIndustry.INSURANCE,
        ["insurancereinsurance"] = GicsIndustry.INSURANCE,
        ["insurancespecialty"] = GicsIndustry.INSURANCE,
        ["capitalmarkets"] = GicsIndustry.CAPITAL_MARKETS,
        ["assetmanagement"] = GicsIndustry.CAPITAL_MARKETS,
        ["financialdatastockexchanges"] = GicsIndustry.CAPITAL_MARKETS,
        ["creditservices"] = GicsIndustry.CONSUMER_FINANCE,

        // Health Care
        ["drugmanufacturersgeneral"] = GicsIndustry.PHARMACEUTICALS,
        ["drugmanufacturersspecialtygeneric"] = GicsIndustry.PHARMACEUTICALS,
        ["biotechnology"] = GicsIndustry.BIOTECHNOLOGY,
        ["medicaldevices"] = GicsIndustry.HEALTH_CARE_EQUIPMENT_AND_SUPPLIES,
        ["medicalinstrumentssupplies"] = GicsIndustry.HEALTH_CARE_EQUIPMENT_AND_SUPPLIES,
        ["healthcareplans"] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        ["medicalcarefacilities"] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        ["healthinformationservices"] = GicsIndustry.HEALTH_CARE_TECHNOLOGY,
        ["diagnosticsresearch"] = GicsIndustry.LIFE_SCIENCES_TOOLS_AND_SERVICES,

        // Energy
        ["oilgasintegrated"] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        ["oilgasep"] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        ["oilgasmidstream"] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        ["oilgasrefiningmarketing"] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        ["thermalcoal"] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        ["oilgasequipmentservices"] = GicsIndustry.ENERGY_EQUIPMENT_AND_SERVICES,
        ["oilgasdrilling"] = GicsIndustry.ENERGY_EQUIPMENT_AND_SERVICES,

        // Consumer Discretionary
        ["automanufacturers"] = GicsIndustry.AUTOMOBILES,
        ["autoparts"] = GicsIndustry.AUTOMOBILE_COMPONENTS,
        ["specialtyretail"] = GicsIndustry.SPECIALTY_RETAIL,
        ["apparelretail"] = GicsIndustry.SPECIALTY_RETAIL,
        ["homeimprovementretail"] = GicsIndustry.SPECIALTY_RETAIL,
        ["internetretail"] = GicsIndustry.BROADLINE_RETAIL,
        ["restaurants"] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        ["lodging"] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        ["resortscasinos"] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        ["travelservices"] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        ["apparelmanufacturing"] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        ["apparelmanufacturers"] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        ["luxurygoods"] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        ["footwearaccessories"] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        ["furnishingsfixturesappliances"] = GicsIndustry.HOUSEHOLD_DURABLES,
        ["residentialconstruction"] = GicsIndustry.HOUSEHOLD_DURABLES,
        ["leisure"] = GicsIndustry.LEISURE_PRODUCTS,

        // Consumer Staples
        ["discountstores"] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        ["grocerystores"] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        ["fooddistribution"] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        ["beveragesnonalcoholic"] = GicsIndustry.BEVERAGES,
        ["beverageswineriesdistilleries"] = GicsIndustry.BEVERAGES,
        ["beveragesbrewers"] = GicsIndustry.BEVERAGES,
        ["packagedfoods"] = GicsIndustry.FOOD_PRODUCTS,
        ["confectioners"] = GicsIndustry.FOOD_PRODUCTS,
        ["farmproducts"] = GicsIndustry.FOOD_PRODUCTS,
        ["tobacco"] = GicsIndustry.TOBACCO,
        ["householdpersonalproducts"] = GicsIndustry.HOUSEHOLD_PRODUCTS,

        // Materials
        ["chemicals"] = GicsIndustry.CHEMICALS,
        ["specialtychemicals"] = GicsIndustry.CHEMICALS,
        ["agriculturalinputs"] = GicsIndustry.CHEMICALS,
        ["buildingmaterials"] = GicsIndustry.CONSTRUCTION_MATERIALS,
        ["steel"] = GicsIndustry.METALS_AND_MINING,
        ["aluminum"] = GicsIndustry.METALS_AND_MINING,
        ["copper"] = GicsIndustry.METALS_AND_MINING,
        ["gold"] = GicsIndustry.METALS_AND_MINING,
        ["otherindustrialmetalsmining"] = GicsIndustry.METALS_AND_MINING,
        ["otherpreciousmetalsmining"] = GicsIndustry.METALS_AND_MINING,
        ["paperpaperproducts"] = GicsIndustry.PAPER_AND_FOREST_PRODUCTS,
        ["lumberwoodproduction"] = GicsIndustry.PAPER_AND_FOREST_PRODUCTS,
        ["packagingcontainers"] = GicsIndustry.CONTAINERS_AND_PACKAGING,

        // Industrials
        ["aerospacedefense"] = GicsIndustry.AEROSPACE_AND_DEFENSE,
        ["railroads"] = GicsIndustry.GROUND_TRANSPORTATION,
        ["trucking"] = GicsIndustry.GROUND_TRANSPORTATION,
        ["airlines"] = GicsIndustry.PASSENGER_AIRLINES,
        ["integratedfreightlogistics"] = GicsIndustry.AIR_FREIGHT_AND_LOGISTICS,
        ["marineshipping"] = GicsIndustry.MARINE_TRANSPORTATION,
        ["industrialdistribution"] = GicsIndustry.TRADING_COMPANIES_AND_DISTRIBUTORS,
        ["specialtyindustrialmachinery"] = GicsIndustry.MACHINERY,
        ["farmheavyconstructionmachinery"] = GicsIndustry.MACHINERY,
        ["toolsaccessories"] = GicsIndustry.MACHINERY,
        ["electricalequipmentparts"] = GicsIndustry.ELECTRICAL_EQUIPMENT,
        ["engineeringconstruction"] = GicsIndustry.CONSTRUCTION_AND_ENGINEERING,
        ["buildingproductsequipment"] = GicsIndustry.BUILDING_PRODUCTS,
        ["conglomerates"] = GicsIndustry.INDUSTRIAL_CONGLOMERATES,
        ["consultingservices"] = GicsIndustry.PROFESSIONAL_SERVICES,
        ["staffingemploymentservices"] = GicsIndustry.PROFESSIONAL_SERVICES,
        ["securityprotectionservices"] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        ["wastemanagement"] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,

        // Utilities
        ["utilitiesregulatedelectric"] = GicsIndustry.ELECTRIC_UTILITIES,
        ["utilitiesindependentpowerproducers"] = GicsIndustry.INDEPENDENT_POWER_AND_RENEWABLE_ELECTRICITY_PRODUCERS,
        ["utilitiesrenewable"] = GicsIndustry.INDEPENDENT_POWER_AND_RENEWABLE_ELECTRICITY_PRODUCERS,
        ["utilitiesregulatedgas"] = GicsIndustry.GAS_UTILITIES,
        ["utilitiesdiversified"] = GicsIndustry.MULTI_UTILITIES,
        ["utilitiesregulatedwater"] = GicsIndustry.WATER_UTILITIES,

        // Real Estate
        ["reitresidential"] = GicsIndustry.RESIDENTIAL_REITS,
        ["reitretail"] = GicsIndustry.RETAIL_REITS,
        ["reitoffice"] = GicsIndustry.OFFICE_REITS,
        ["reitindustrial"] = GicsIndustry.INDUSTRIAL_REITS,
        ["reithealthcarefacilities"] = GicsIndustry.HEALTH_CARE_REITS,
        ["reithotelmotel"] = GicsIndustry.HOTEL_AND_RESORT_REITS,
        ["reitdiversified"] = GicsIndustry.DIVERSIFIED_REITS,
        ["reitspecialty"] = GicsIndustry.SPECIALIZED_REITS,
        ["reitmortgage"] = GicsIndustry.MORTGAGE_REAL_ESTATE_INVESTMENT_TRUSTS,
        ["realestateservices"] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
        ["realestatedevelopment"] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
        ["realestatediversified"] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT
    };
}
