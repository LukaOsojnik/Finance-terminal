namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// The finest GICS tier (2023 structure): 163 sub-industries, each rolling up to exactly one
/// <see cref="GicsIndustry"/>. This is the only tier the LLM reasons to from a vendor (FMP) label;
/// Industry and Sector then fall out deterministically via <see cref="GicsSubIndustryExtensions"/>.
/// Members are grouped by their parent GICS Industry (the comment headers).
/// </summary>
public enum GicsSubIndustry
{
    // ── ENERGY ──
    // Energy Equipment & Services
    OIL_AND_GAS_DRILLING,
    OIL_AND_GAS_EQUIPMENT_AND_SERVICES,
    // Oil, Gas & Consumable Fuels
    INTEGRATED_OIL_AND_GAS,
    OIL_AND_GAS_EXPLORATION_AND_PRODUCTION,
    OIL_AND_GAS_REFINING_AND_MARKETING,
    OIL_AND_GAS_STORAGE_AND_TRANSPORTATION,
    COAL_AND_CONSUMABLE_FUELS,

    // ── MATERIALS ──
    // Chemicals
    COMMODITY_CHEMICALS,
    DIVERSIFIED_CHEMICALS,
    FERTILIZERS_AND_AGRICULTURAL_CHEMICALS,
    INDUSTRIAL_GASES,
    SPECIALTY_CHEMICALS,
    // Construction Materials
    CONSTRUCTION_MATERIALS_SUB,
    // Containers & Packaging
    METAL_GLASS_AND_PLASTIC_CONTAINERS,
    PAPER_AND_PLASTIC_PACKAGING_PRODUCTS_AND_MATERIALS,
    // Metals & Mining
    ALUMINUM,
    DIVERSIFIED_METALS_AND_MINING,
    COPPER,
    GOLD,
    PRECIOUS_METALS_AND_MINERALS,
    SILVER,
    STEEL,
    // Paper & Forest Products
    FOREST_PRODUCTS,
    PAPER_PRODUCTS,

    // ── INDUSTRIALS ──
    // Aerospace & Defense
    AEROSPACE_AND_DEFENSE_SUB,
    // Building Products
    BUILDING_PRODUCTS_SUB,
    // Construction & Engineering
    CONSTRUCTION_AND_ENGINEERING_SUB,
    // Electrical Equipment
    ELECTRICAL_COMPONENTS_AND_EQUIPMENT,
    HEAVY_ELECTRICAL_EQUIPMENT,
    // Industrial Conglomerates
    INDUSTRIAL_CONGLOMERATES_SUB,
    // Machinery
    CONSTRUCTION_MACHINERY_AND_HEAVY_TRANSPORTATION_EQUIPMENT,
    AGRICULTURAL_AND_FARM_MACHINERY,
    INDUSTRIAL_MACHINERY_AND_SUPPLIES_AND_COMPONENTS,
    // Trading Companies & Distributors
    TRADING_COMPANIES_AND_DISTRIBUTORS_SUB,
    // Commercial Services & Supplies
    COMMERCIAL_PRINTING,
    ENVIRONMENTAL_AND_FACILITIES_SERVICES,
    OFFICE_SERVICES_AND_SUPPLIES,
    DIVERSIFIED_SUPPORT_SERVICES,
    SECURITY_AND_ALARM_SERVICES,
    // Professional Services
    HUMAN_RESOURCE_AND_EMPLOYMENT_SERVICES,
    RESEARCH_AND_CONSULTING_SERVICES,
    // Air Freight & Logistics
    AIR_FREIGHT_AND_LOGISTICS_SUB,
    // Passenger Airlines
    PASSENGER_AIRLINES_SUB,
    // Marine Transportation
    MARINE_TRANSPORTATION_SUB,
    // Ground Transportation
    RAIL_TRANSPORTATION,
    CARGO_GROUND_TRANSPORTATION,
    PASSENGER_GROUND_TRANSPORTATION,
    // Transportation Infrastructure
    AIRPORT_SERVICES,
    HIGHWAYS_AND_RAILTRACKS,
    MARINE_PORTS_AND_SERVICES,

    // ── CONSUMER DISCRETIONARY ──
    // Automobile Components
    AUTOMOTIVE_PARTS_AND_EQUIPMENT,
    TIRES_AND_RUBBER,
    // Automobiles
    AUTOMOBILE_MANUFACTURERS,
    MOTORCYCLE_MANUFACTURERS,
    // Household Durables
    CONSUMER_ELECTRONICS,
    HOME_FURNISHINGS,
    HOMEBUILDING,
    HOUSEHOLD_APPLIANCES,
    HOUSEWARES_AND_SPECIALTIES,
    // Leisure Products
    LEISURE_PRODUCTS_SUB,
    // Textiles, Apparel & Luxury Goods
    APPAREL_ACCESSORIES_AND_LUXURY_GOODS,
    FOOTWEAR,
    TEXTILES,
    // Hotels, Restaurants & Leisure
    CASINOS_AND_GAMING,
    HOTELS_RESORTS_AND_CRUISE_LINES,
    LEISURE_FACILITIES,
    RESTAURANTS,
    // Diversified Consumer Services
    EDUCATION_SERVICES,
    SPECIALIZED_CONSUMER_SERVICES,
    // Distributors
    DISTRIBUTORS_SUB,
    // Broadline Retail
    BROADLINE_RETAIL_SUB,
    // Specialty Retail
    APPAREL_RETAIL,
    COMPUTER_AND_ELECTRONICS_RETAIL,
    HOME_IMPROVEMENT_RETAIL,
    OTHER_SPECIALTY_RETAIL,
    AUTOMOTIVE_RETAIL,
    HOMEFURNISHING_RETAIL,

    // ── CONSUMER STAPLES ──
    // Consumer Staples Distribution & Retail
    DRUG_RETAIL,
    FOOD_DISTRIBUTORS,
    FOOD_RETAIL,
    CONSUMER_STAPLES_MERCHANDISE_RETAIL,
    // Beverages
    BREWERS,
    DISTILLERS_AND_VINTNERS,
    SOFT_DRINKS_AND_NON_ALCOHOLIC_BEVERAGES,
    // Food Products
    AGRICULTURAL_PRODUCTS_AND_SERVICES,
    PACKAGED_FOODS_AND_MEATS,
    // Tobacco
    TOBACCO_SUB,
    // Household Products
    HOUSEHOLD_PRODUCTS_SUB,
    // Personal Care Products
    PERSONAL_CARE_PRODUCTS_SUB,

    // ── HEALTH CARE ──
    // Health Care Equipment & Supplies
    HEALTH_CARE_EQUIPMENT,
    HEALTH_CARE_SUPPLIES,
    // Health Care Providers & Services
    HEALTH_CARE_DISTRIBUTORS,
    HEALTH_CARE_SERVICES,
    HEALTH_CARE_FACILITIES,
    MANAGED_HEALTH_CARE,
    // Health Care Technology
    HEALTH_CARE_TECHNOLOGY_SUB,
    // Biotechnology
    BIOTECHNOLOGY_SUB,
    // Pharmaceuticals
    PHARMACEUTICALS_SUB,
    // Life Sciences Tools & Services
    LIFE_SCIENCES_TOOLS_AND_SERVICES_SUB,

    // ── FINANCIALS ──
    // Banks
    DIVERSIFIED_BANKS,
    REGIONAL_BANKS,
    // Financial Services
    DIVERSIFIED_FINANCIAL_SERVICES,
    MULTI_SECTOR_HOLDINGS,
    SPECIALIZED_FINANCE,
    COMMERCIAL_AND_RESIDENTIAL_MORTGAGE_FINANCE,
    TRANSACTION_AND_PAYMENT_PROCESSING_SERVICES,
    // Consumer Finance
    CONSUMER_FINANCE_SUB,
    // Capital Markets
    ASSET_MANAGEMENT_AND_CUSTODY_BANKS,
    INVESTMENT_BANKING_AND_BROKERAGE,
    DIVERSIFIED_CAPITAL_MARKETS,
    FINANCIAL_EXCHANGES_AND_DATA,
    // Mortgage REITs
    MORTGAGE_REITS,
    // Insurance
    INSURANCE_BROKERS,
    LIFE_AND_HEALTH_INSURANCE,
    MULTI_LINE_INSURANCE,
    PROPERTY_AND_CASUALTY_INSURANCE,
    REINSURANCE,

    // ── INFORMATION TECHNOLOGY ──
    // IT Services
    IT_CONSULTING_AND_OTHER_SERVICES,
    INTERNET_SERVICES_AND_INFRASTRUCTURE,
    // Software
    APPLICATION_SOFTWARE,
    SYSTEMS_SOFTWARE,
    // Communications Equipment
    COMMUNICATIONS_EQUIPMENT_SUB,
    // Technology Hardware, Storage & Peripherals
    TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS_SUB,
    // Electronic Equipment, Instruments & Components
    ELECTRONIC_EQUIPMENT_AND_INSTRUMENTS,
    ELECTRONIC_COMPONENTS,
    ELECTRONIC_MANUFACTURING_SERVICES,
    TECHNOLOGY_DISTRIBUTORS,
    // Semiconductors & Semiconductor Equipment
    SEMICONDUCTOR_MATERIALS_AND_EQUIPMENT,
    SEMICONDUCTORS,

    // ── COMMUNICATION SERVICES ──
    // Diversified Telecommunication Services
    ALTERNATIVE_CARRIERS,
    INTEGRATED_TELECOMMUNICATION_SERVICES,
    // Wireless Telecommunication Services
    WIRELESS_TELECOMMUNICATION_SERVICES_SUB,
    // Media
    ADVERTISING,
    BROADCASTING,
    CABLE_AND_SATELLITE,
    PUBLISHING,
    // Entertainment
    MOVIES_AND_ENTERTAINMENT,
    INTERACTIVE_HOME_ENTERTAINMENT,
    // Interactive Media & Services
    INTERACTIVE_MEDIA_AND_SERVICES_SUB,

    // ── UTILITIES ──
    // Electric Utilities
    ELECTRIC_UTILITIES_SUB,
    // Gas Utilities
    GAS_UTILITIES_SUB,
    // Multi-Utilities
    MULTI_UTILITIES_SUB,
    // Water Utilities
    WATER_UTILITIES_SUB,
    // Independent Power and Renewable Electricity Producers
    INDEPENDENT_POWER_PRODUCERS_AND_ENERGY_TRADERS,
    RENEWABLE_ELECTRICITY,

    // ── REAL ESTATE ──
    // Diversified REITs
    DIVERSIFIED_REITS_SUB,
    // Industrial REITs
    INDUSTRIAL_REITS_SUB,
    // Hotel & Resort REITs
    HOTEL_AND_RESORT_REITS_SUB,
    // Office REITs
    OFFICE_REITS_SUB,
    // Health Care REITs
    HEALTH_CARE_REITS_SUB,
    // Residential REITs
    MULTI_FAMILY_RESIDENTIAL_REITS,
    SINGLE_FAMILY_RESIDENTIAL_REITS,
    // Retail REITs
    RETAIL_REITS_SUB,
    // Specialized REITs
    OTHER_SPECIALIZED_REITS,
    SELF_STORAGE_REITS,
    TELECOM_TOWER_REITS,
    TIMBER_REITS,
    DATA_CENTER_REITS,
    // Real Estate Management & Development
    DIVERSIFIED_REAL_ESTATE_ACTIVITIES,
    REAL_ESTATE_OPERATING_COMPANIES,
    REAL_ESTATE_DEVELOPMENT,
    REAL_ESTATE_SERVICES,

    // ── Appended out of sector-group on purpose ──
    // GICS 2023 (code 20202030) "Data Processing & Outsourced Services" lives under PROFESSIONAL SERVICES
    // (Industrials) — it moved there from IT Services in the 2023 revision. It belongs logically beside
    // RESEARCH_AND_CONSULTING_SERVICES, but the enum ORDINAL is the value persisted in
    // Company.GicsSubIndustry / FmpIndustryMapping, so a new member MUST be appended last: inserting it
    // in place would shift every later ordinal and silently corrupt stored rows.
    DATA_PROCESSING_AND_OUTSOURCED_SERVICES
}

public static class GicsSubIndustryExtensions
{
    // The GICS hierarchy: each sub-industry rolls up to exactly one Industry (and through it, one
    // Sector). Mirrors the official 2023 structure; the parent Industry reuses the existing 74-value
    // GicsIndustry enum so Company.Industry stays a denormalized cache of this rollup.
    private static readonly Dictionary<GicsSubIndustry, GicsIndustry> IndustryMap = new()
    {
        // Energy
        [GicsSubIndustry.OIL_AND_GAS_DRILLING] = GicsIndustry.ENERGY_EQUIPMENT_AND_SERVICES,
        [GicsSubIndustry.OIL_AND_GAS_EQUIPMENT_AND_SERVICES] = GicsIndustry.ENERGY_EQUIPMENT_AND_SERVICES,
        [GicsSubIndustry.INTEGRATED_OIL_AND_GAS] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        [GicsSubIndustry.OIL_AND_GAS_EXPLORATION_AND_PRODUCTION] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        [GicsSubIndustry.OIL_AND_GAS_REFINING_AND_MARKETING] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        [GicsSubIndustry.OIL_AND_GAS_STORAGE_AND_TRANSPORTATION] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,
        [GicsSubIndustry.COAL_AND_CONSUMABLE_FUELS] = GicsIndustry.OIL_GAS_AND_CONSUMABLE_FUELS,

        // Materials
        [GicsSubIndustry.COMMODITY_CHEMICALS] = GicsIndustry.CHEMICALS,
        [GicsSubIndustry.DIVERSIFIED_CHEMICALS] = GicsIndustry.CHEMICALS,
        [GicsSubIndustry.FERTILIZERS_AND_AGRICULTURAL_CHEMICALS] = GicsIndustry.CHEMICALS,
        [GicsSubIndustry.INDUSTRIAL_GASES] = GicsIndustry.CHEMICALS,
        [GicsSubIndustry.SPECIALTY_CHEMICALS] = GicsIndustry.CHEMICALS,
        [GicsSubIndustry.CONSTRUCTION_MATERIALS_SUB] = GicsIndustry.CONSTRUCTION_MATERIALS,
        [GicsSubIndustry.METAL_GLASS_AND_PLASTIC_CONTAINERS] = GicsIndustry.CONTAINERS_AND_PACKAGING,
        [GicsSubIndustry.PAPER_AND_PLASTIC_PACKAGING_PRODUCTS_AND_MATERIALS] = GicsIndustry.CONTAINERS_AND_PACKAGING,
        [GicsSubIndustry.ALUMINUM] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.DIVERSIFIED_METALS_AND_MINING] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.COPPER] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.GOLD] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.PRECIOUS_METALS_AND_MINERALS] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.SILVER] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.STEEL] = GicsIndustry.METALS_AND_MINING,
        [GicsSubIndustry.FOREST_PRODUCTS] = GicsIndustry.PAPER_AND_FOREST_PRODUCTS,
        [GicsSubIndustry.PAPER_PRODUCTS] = GicsIndustry.PAPER_AND_FOREST_PRODUCTS,

        // Industrials
        [GicsSubIndustry.AEROSPACE_AND_DEFENSE_SUB] = GicsIndustry.AEROSPACE_AND_DEFENSE,
        [GicsSubIndustry.BUILDING_PRODUCTS_SUB] = GicsIndustry.BUILDING_PRODUCTS,
        [GicsSubIndustry.CONSTRUCTION_AND_ENGINEERING_SUB] = GicsIndustry.CONSTRUCTION_AND_ENGINEERING,
        [GicsSubIndustry.ELECTRICAL_COMPONENTS_AND_EQUIPMENT] = GicsIndustry.ELECTRICAL_EQUIPMENT,
        [GicsSubIndustry.HEAVY_ELECTRICAL_EQUIPMENT] = GicsIndustry.ELECTRICAL_EQUIPMENT,
        [GicsSubIndustry.INDUSTRIAL_CONGLOMERATES_SUB] = GicsIndustry.INDUSTRIAL_CONGLOMERATES,
        [GicsSubIndustry.CONSTRUCTION_MACHINERY_AND_HEAVY_TRANSPORTATION_EQUIPMENT] = GicsIndustry.MACHINERY,
        [GicsSubIndustry.AGRICULTURAL_AND_FARM_MACHINERY] = GicsIndustry.MACHINERY,
        [GicsSubIndustry.INDUSTRIAL_MACHINERY_AND_SUPPLIES_AND_COMPONENTS] = GicsIndustry.MACHINERY,
        [GicsSubIndustry.TRADING_COMPANIES_AND_DISTRIBUTORS_SUB] = GicsIndustry.TRADING_COMPANIES_AND_DISTRIBUTORS,
        [GicsSubIndustry.COMMERCIAL_PRINTING] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        [GicsSubIndustry.ENVIRONMENTAL_AND_FACILITIES_SERVICES] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        [GicsSubIndustry.OFFICE_SERVICES_AND_SUPPLIES] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        [GicsSubIndustry.DIVERSIFIED_SUPPORT_SERVICES] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        [GicsSubIndustry.SECURITY_AND_ALARM_SERVICES] = GicsIndustry.COMMERCIAL_SERVICES_AND_SUPPLIES,
        [GicsSubIndustry.HUMAN_RESOURCE_AND_EMPLOYMENT_SERVICES] = GicsIndustry.PROFESSIONAL_SERVICES,
        [GicsSubIndustry.RESEARCH_AND_CONSULTING_SERVICES] = GicsIndustry.PROFESSIONAL_SERVICES,
        // Declared last in the enum (ordinal stability) but rolls up here — GICS 2023 code 20202030.
        [GicsSubIndustry.DATA_PROCESSING_AND_OUTSOURCED_SERVICES] = GicsIndustry.PROFESSIONAL_SERVICES,
        [GicsSubIndustry.AIR_FREIGHT_AND_LOGISTICS_SUB] = GicsIndustry.AIR_FREIGHT_AND_LOGISTICS,
        [GicsSubIndustry.PASSENGER_AIRLINES_SUB] = GicsIndustry.PASSENGER_AIRLINES,
        [GicsSubIndustry.MARINE_TRANSPORTATION_SUB] = GicsIndustry.MARINE_TRANSPORTATION,
        [GicsSubIndustry.RAIL_TRANSPORTATION] = GicsIndustry.GROUND_TRANSPORTATION,
        [GicsSubIndustry.CARGO_GROUND_TRANSPORTATION] = GicsIndustry.GROUND_TRANSPORTATION,
        [GicsSubIndustry.PASSENGER_GROUND_TRANSPORTATION] = GicsIndustry.GROUND_TRANSPORTATION,
        [GicsSubIndustry.AIRPORT_SERVICES] = GicsIndustry.TRANSPORTATION_INFRASTRUCTURE,
        [GicsSubIndustry.HIGHWAYS_AND_RAILTRACKS] = GicsIndustry.TRANSPORTATION_INFRASTRUCTURE,
        [GicsSubIndustry.MARINE_PORTS_AND_SERVICES] = GicsIndustry.TRANSPORTATION_INFRASTRUCTURE,

        // Consumer Discretionary
        [GicsSubIndustry.AUTOMOTIVE_PARTS_AND_EQUIPMENT] = GicsIndustry.AUTOMOBILE_COMPONENTS,
        [GicsSubIndustry.TIRES_AND_RUBBER] = GicsIndustry.AUTOMOBILE_COMPONENTS,
        [GicsSubIndustry.AUTOMOBILE_MANUFACTURERS] = GicsIndustry.AUTOMOBILES,
        [GicsSubIndustry.MOTORCYCLE_MANUFACTURERS] = GicsIndustry.AUTOMOBILES,
        [GicsSubIndustry.CONSUMER_ELECTRONICS] = GicsIndustry.HOUSEHOLD_DURABLES,
        [GicsSubIndustry.HOME_FURNISHINGS] = GicsIndustry.HOUSEHOLD_DURABLES,
        [GicsSubIndustry.HOMEBUILDING] = GicsIndustry.HOUSEHOLD_DURABLES,
        [GicsSubIndustry.HOUSEHOLD_APPLIANCES] = GicsIndustry.HOUSEHOLD_DURABLES,
        [GicsSubIndustry.HOUSEWARES_AND_SPECIALTIES] = GicsIndustry.HOUSEHOLD_DURABLES,
        [GicsSubIndustry.LEISURE_PRODUCTS_SUB] = GicsIndustry.LEISURE_PRODUCTS,
        [GicsSubIndustry.APPAREL_ACCESSORIES_AND_LUXURY_GOODS] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        [GicsSubIndustry.FOOTWEAR] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        [GicsSubIndustry.TEXTILES] = GicsIndustry.TEXTILES_APPAREL_AND_LUXURY_GOODS,
        [GicsSubIndustry.CASINOS_AND_GAMING] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        [GicsSubIndustry.HOTELS_RESORTS_AND_CRUISE_LINES] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        [GicsSubIndustry.LEISURE_FACILITIES] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        [GicsSubIndustry.RESTAURANTS] = GicsIndustry.HOTELS_RESTAURANTS_AND_LEISURE,
        [GicsSubIndustry.EDUCATION_SERVICES] = GicsIndustry.DIVERSIFIED_CONSUMER_SERVICES,
        [GicsSubIndustry.SPECIALIZED_CONSUMER_SERVICES] = GicsIndustry.DIVERSIFIED_CONSUMER_SERVICES,
        [GicsSubIndustry.DISTRIBUTORS_SUB] = GicsIndustry.DISTRIBUTORS,
        [GicsSubIndustry.BROADLINE_RETAIL_SUB] = GicsIndustry.BROADLINE_RETAIL,
        [GicsSubIndustry.APPAREL_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,
        [GicsSubIndustry.COMPUTER_AND_ELECTRONICS_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,
        [GicsSubIndustry.HOME_IMPROVEMENT_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,
        [GicsSubIndustry.OTHER_SPECIALTY_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,
        [GicsSubIndustry.AUTOMOTIVE_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,
        [GicsSubIndustry.HOMEFURNISHING_RETAIL] = GicsIndustry.SPECIALTY_RETAIL,

        // Consumer Staples
        [GicsSubIndustry.DRUG_RETAIL] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        [GicsSubIndustry.FOOD_DISTRIBUTORS] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        [GicsSubIndustry.FOOD_RETAIL] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        [GicsSubIndustry.CONSUMER_STAPLES_MERCHANDISE_RETAIL] = GicsIndustry.CONSUMER_STAPLES_DISTRIBUTION_AND_RETAIL,
        [GicsSubIndustry.BREWERS] = GicsIndustry.BEVERAGES,
        [GicsSubIndustry.DISTILLERS_AND_VINTNERS] = GicsIndustry.BEVERAGES,
        [GicsSubIndustry.SOFT_DRINKS_AND_NON_ALCOHOLIC_BEVERAGES] = GicsIndustry.BEVERAGES,
        [GicsSubIndustry.AGRICULTURAL_PRODUCTS_AND_SERVICES] = GicsIndustry.FOOD_PRODUCTS,
        [GicsSubIndustry.PACKAGED_FOODS_AND_MEATS] = GicsIndustry.FOOD_PRODUCTS,
        [GicsSubIndustry.TOBACCO_SUB] = GicsIndustry.TOBACCO,
        [GicsSubIndustry.HOUSEHOLD_PRODUCTS_SUB] = GicsIndustry.HOUSEHOLD_PRODUCTS,
        [GicsSubIndustry.PERSONAL_CARE_PRODUCTS_SUB] = GicsIndustry.PERSONAL_CARE_PRODUCTS,

        // Health Care
        [GicsSubIndustry.HEALTH_CARE_EQUIPMENT] = GicsIndustry.HEALTH_CARE_EQUIPMENT_AND_SUPPLIES,
        [GicsSubIndustry.HEALTH_CARE_SUPPLIES] = GicsIndustry.HEALTH_CARE_EQUIPMENT_AND_SUPPLIES,
        [GicsSubIndustry.HEALTH_CARE_DISTRIBUTORS] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        [GicsSubIndustry.HEALTH_CARE_SERVICES] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        [GicsSubIndustry.HEALTH_CARE_FACILITIES] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        [GicsSubIndustry.MANAGED_HEALTH_CARE] = GicsIndustry.HEALTH_CARE_PROVIDERS_AND_SERVICES,
        [GicsSubIndustry.HEALTH_CARE_TECHNOLOGY_SUB] = GicsIndustry.HEALTH_CARE_TECHNOLOGY,
        [GicsSubIndustry.BIOTECHNOLOGY_SUB] = GicsIndustry.BIOTECHNOLOGY,
        [GicsSubIndustry.PHARMACEUTICALS_SUB] = GicsIndustry.PHARMACEUTICALS,
        [GicsSubIndustry.LIFE_SCIENCES_TOOLS_AND_SERVICES_SUB] = GicsIndustry.LIFE_SCIENCES_TOOLS_AND_SERVICES,

        // Financials
        [GicsSubIndustry.DIVERSIFIED_BANKS] = GicsIndustry.BANKS,
        [GicsSubIndustry.REGIONAL_BANKS] = GicsIndustry.BANKS,
        [GicsSubIndustry.DIVERSIFIED_FINANCIAL_SERVICES] = GicsIndustry.FINANCIAL_SERVICES,
        [GicsSubIndustry.MULTI_SECTOR_HOLDINGS] = GicsIndustry.FINANCIAL_SERVICES,
        [GicsSubIndustry.SPECIALIZED_FINANCE] = GicsIndustry.FINANCIAL_SERVICES,
        [GicsSubIndustry.COMMERCIAL_AND_RESIDENTIAL_MORTGAGE_FINANCE] = GicsIndustry.FINANCIAL_SERVICES,
        [GicsSubIndustry.TRANSACTION_AND_PAYMENT_PROCESSING_SERVICES] = GicsIndustry.FINANCIAL_SERVICES,
        [GicsSubIndustry.CONSUMER_FINANCE_SUB] = GicsIndustry.CONSUMER_FINANCE,
        [GicsSubIndustry.ASSET_MANAGEMENT_AND_CUSTODY_BANKS] = GicsIndustry.CAPITAL_MARKETS,
        [GicsSubIndustry.INVESTMENT_BANKING_AND_BROKERAGE] = GicsIndustry.CAPITAL_MARKETS,
        [GicsSubIndustry.DIVERSIFIED_CAPITAL_MARKETS] = GicsIndustry.CAPITAL_MARKETS,
        [GicsSubIndustry.FINANCIAL_EXCHANGES_AND_DATA] = GicsIndustry.CAPITAL_MARKETS,
        [GicsSubIndustry.MORTGAGE_REITS] = GicsIndustry.MORTGAGE_REAL_ESTATE_INVESTMENT_TRUSTS,
        [GicsSubIndustry.INSURANCE_BROKERS] = GicsIndustry.INSURANCE,
        [GicsSubIndustry.LIFE_AND_HEALTH_INSURANCE] = GicsIndustry.INSURANCE,
        [GicsSubIndustry.MULTI_LINE_INSURANCE] = GicsIndustry.INSURANCE,
        [GicsSubIndustry.PROPERTY_AND_CASUALTY_INSURANCE] = GicsIndustry.INSURANCE,
        [GicsSubIndustry.REINSURANCE] = GicsIndustry.INSURANCE,

        // Information Technology
        [GicsSubIndustry.IT_CONSULTING_AND_OTHER_SERVICES] = GicsIndustry.IT_SERVICES,
        [GicsSubIndustry.INTERNET_SERVICES_AND_INFRASTRUCTURE] = GicsIndustry.IT_SERVICES,
        [GicsSubIndustry.APPLICATION_SOFTWARE] = GicsIndustry.SOFTWARE,
        [GicsSubIndustry.SYSTEMS_SOFTWARE] = GicsIndustry.SOFTWARE,
        [GicsSubIndustry.COMMUNICATIONS_EQUIPMENT_SUB] = GicsIndustry.COMMUNICATIONS_EQUIPMENT,
        [GicsSubIndustry.TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS_SUB] = GicsIndustry.TECHNOLOGY_HARDWARE_STORAGE_AND_PERIPHERALS,
        [GicsSubIndustry.ELECTRONIC_EQUIPMENT_AND_INSTRUMENTS] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,
        [GicsSubIndustry.ELECTRONIC_COMPONENTS] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,
        [GicsSubIndustry.ELECTRONIC_MANUFACTURING_SERVICES] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,
        [GicsSubIndustry.TECHNOLOGY_DISTRIBUTORS] = GicsIndustry.ELECTRONIC_EQUIPMENT_INSTRUMENTS_AND_COMPONENTS,
        [GicsSubIndustry.SEMICONDUCTOR_MATERIALS_AND_EQUIPMENT] = GicsIndustry.SEMICONDUCTORS_AND_SEMICONDUCTOR_EQUIPMENT,
        [GicsSubIndustry.SEMICONDUCTORS] = GicsIndustry.SEMICONDUCTORS_AND_SEMICONDUCTOR_EQUIPMENT,

        // Communication Services
        [GicsSubIndustry.ALTERNATIVE_CARRIERS] = GicsIndustry.DIVERSIFIED_TELECOMMUNICATION_SERVICES,
        [GicsSubIndustry.INTEGRATED_TELECOMMUNICATION_SERVICES] = GicsIndustry.DIVERSIFIED_TELECOMMUNICATION_SERVICES,
        [GicsSubIndustry.WIRELESS_TELECOMMUNICATION_SERVICES_SUB] = GicsIndustry.WIRELESS_TELECOMMUNICATION_SERVICES,
        [GicsSubIndustry.ADVERTISING] = GicsIndustry.MEDIA,
        [GicsSubIndustry.BROADCASTING] = GicsIndustry.MEDIA,
        [GicsSubIndustry.CABLE_AND_SATELLITE] = GicsIndustry.MEDIA,
        [GicsSubIndustry.PUBLISHING] = GicsIndustry.MEDIA,
        [GicsSubIndustry.MOVIES_AND_ENTERTAINMENT] = GicsIndustry.ENTERTAINMENT,
        [GicsSubIndustry.INTERACTIVE_HOME_ENTERTAINMENT] = GicsIndustry.ENTERTAINMENT,
        [GicsSubIndustry.INTERACTIVE_MEDIA_AND_SERVICES_SUB] = GicsIndustry.INTERACTIVE_MEDIA_AND_SERVICES,

        // Utilities
        [GicsSubIndustry.ELECTRIC_UTILITIES_SUB] = GicsIndustry.ELECTRIC_UTILITIES,
        [GicsSubIndustry.GAS_UTILITIES_SUB] = GicsIndustry.GAS_UTILITIES,
        [GicsSubIndustry.MULTI_UTILITIES_SUB] = GicsIndustry.MULTI_UTILITIES,
        [GicsSubIndustry.WATER_UTILITIES_SUB] = GicsIndustry.WATER_UTILITIES,
        [GicsSubIndustry.INDEPENDENT_POWER_PRODUCERS_AND_ENERGY_TRADERS] = GicsIndustry.INDEPENDENT_POWER_AND_RENEWABLE_ELECTRICITY_PRODUCERS,
        [GicsSubIndustry.RENEWABLE_ELECTRICITY] = GicsIndustry.INDEPENDENT_POWER_AND_RENEWABLE_ELECTRICITY_PRODUCERS,

        // Real Estate
        [GicsSubIndustry.DIVERSIFIED_REITS_SUB] = GicsIndustry.DIVERSIFIED_REITS,
        [GicsSubIndustry.INDUSTRIAL_REITS_SUB] = GicsIndustry.INDUSTRIAL_REITS,
        [GicsSubIndustry.HOTEL_AND_RESORT_REITS_SUB] = GicsIndustry.HOTEL_AND_RESORT_REITS,
        [GicsSubIndustry.OFFICE_REITS_SUB] = GicsIndustry.OFFICE_REITS,
        [GicsSubIndustry.HEALTH_CARE_REITS_SUB] = GicsIndustry.HEALTH_CARE_REITS,
        [GicsSubIndustry.MULTI_FAMILY_RESIDENTIAL_REITS] = GicsIndustry.RESIDENTIAL_REITS,
        [GicsSubIndustry.SINGLE_FAMILY_RESIDENTIAL_REITS] = GicsIndustry.RESIDENTIAL_REITS,
        [GicsSubIndustry.RETAIL_REITS_SUB] = GicsIndustry.RETAIL_REITS,
        [GicsSubIndustry.OTHER_SPECIALIZED_REITS] = GicsIndustry.SPECIALIZED_REITS,
        [GicsSubIndustry.SELF_STORAGE_REITS] = GicsIndustry.SPECIALIZED_REITS,
        [GicsSubIndustry.TELECOM_TOWER_REITS] = GicsIndustry.SPECIALIZED_REITS,
        [GicsSubIndustry.TIMBER_REITS] = GicsIndustry.SPECIALIZED_REITS,
        [GicsSubIndustry.DATA_CENTER_REITS] = GicsIndustry.SPECIALIZED_REITS,
        [GicsSubIndustry.DIVERSIFIED_REAL_ESTATE_ACTIVITIES] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
        [GicsSubIndustry.REAL_ESTATE_OPERATING_COMPANIES] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
        [GicsSubIndustry.REAL_ESTATE_DEVELOPMENT] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
        [GicsSubIndustry.REAL_ESTATE_SERVICES] = GicsIndustry.REAL_ESTATE_MANAGEMENT_AND_DEVELOPMENT,
    };

    // Roll a sub-industry up to its parent GICS Industry (and, via that, its Sector).
    public static GicsIndustry GetIndustry(this GicsSubIndustry sub) => IndustryMap[sub];

    public static Sector GetSector(this GicsSubIndustry sub) => IndustryMap[sub].GetSector();
}
