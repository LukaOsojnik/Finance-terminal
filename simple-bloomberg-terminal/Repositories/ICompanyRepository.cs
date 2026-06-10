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
}
