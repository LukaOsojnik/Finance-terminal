using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ICompanyRepository
{
    IEnumerable<Company> GetAll();
    Company? GetById(long id);
    Company? GetWithGraphRelations(long id);
    Company? GetWithGraphRelationsByCik(string cik);
    IEnumerable<Company> Search(string? term);
    IEnumerable<Company> Lookup(string? term);

    // Active company whose name matches after stripping corporate suffixes (Corp/Inc/Ltd…) and
    // punctuation — so "Microsoft Corporation" reuses an existing "Microsoft". Null if none.
    Company? MatchByName(string? name);

    // Active company whose CIK matches (normalised to 10-digit). The canonical join when a name match
    // can't bridge an acronym↔legal-name gap (e.g. "TSMC" vs "Taiwan Semiconductor Manufacturing Co").
    Company? MatchByCik(string? cik);
    void Add(Company entity);
    void Update(Company entity);
    void SoftDelete(long id);

    // Replace a company's financial history with a freshly-fetched set (clear-reinsert, like EDGAR
    // sources). Derived API data, so old rows are hard-deleted; a no-op if rows is empty.
    void ReplaceFinancials(long companyId, IReadOnlyList<CompanyFinancial> rows);

    // Ids of active companies that already have FMP-sourced financials — the backfill's "done" marker.
    // Companies with only YAHOO rows (a quota-degraded fallback) stay eligible so a later run upgrades
    // them to full FMP data once the daily quota resets.
    HashSet<long> CompanyIdsWithFmpFinancials();

    // Map of normalized 10-digit CIK -> company id for every active company that has a CIK. Lets the
    // index importer resolve constituents to existing companies in one pass (first row per CIK wins).
    IReadOnlyDictionary<string, long> CikToIdMap();

    // MarketCap (USD) for the given company ids — the input to cap-weighting an index. Missing/null
    // caps are returned as null so the importer can exclude them from the weight denominator.
    IReadOnlyDictionary<long, double?> MarketCapsByIds(IEnumerable<long> ids);

    // Sector for the given company ids — lets the index importer infer an index's sector from what its
    // members actually are (a fund of all-tech names -> INFORMATION_TECHNOLOGY), since the source files
    // don't carry a reliable index-level sector.
    IReadOnlyDictionary<long, Models.Enums.Sector> SectorsByIds(IEnumerable<long> ids);
}
