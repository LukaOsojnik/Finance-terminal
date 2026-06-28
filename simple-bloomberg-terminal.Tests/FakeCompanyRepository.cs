using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Tiny in-memory <see cref="ICompanyRepository"/> for the Perplexity discovery tests. The discovery
/// service only calls <see cref="GetById"/> (the inspected company) and <see cref="MatchByName"/>
/// (mapping a found counterparty to an existing company), so only those are implemented. Backed by a
/// plain list — no EF DbContext — so the service's parallel sub-query searches can call MatchByName
/// concurrently without tripping "a second operation was started on this context".
/// </summary>
public sealed class FakeCompanyRepository : ICompanyRepository
{
    private readonly List<Company> _companies;
    public FakeCompanyRepository(params Company[] companies) => _companies = [.. companies];

    public Company? GetById(long id) => _companies.FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    // Test-simple match: case-insensitive equality on the trimmed name. The real repo also strips
    // corporate suffixes (Corp/Inc/Ltd); the tests use exact names, so equality proves the link path.
    public Company? MatchByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : _companies.FirstOrDefault(c => c.DeletedAt == null &&
                string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    public Company? MatchByCik(string? cik)
    {
        var norm = Cik.Normalize(cik);
        return norm is null ? null : _companies.FirstOrDefault(c => c.DeletedAt == null &&
            Cik.Normalize(c.Cik) == norm);
    }

    // Unused by the discovery path under test.
    public IReadOnlyList<(string Name, string Cik)> BackfillUsCiksByName(IEnumerable<(string Title, string Cik)> secEntries) => throw new NotSupportedException();
    public IReadOnlyList<CompanyVolumeHistory> GetVolumeHistory(long companyId) => throw new NotSupportedException();
    public void ReplaceVolumeHistory(long companyId, IReadOnlyList<CompanyVolumeHistory> rows) => throw new NotSupportedException();
    public IEnumerable<Company> GetAll() => throw new NotSupportedException();
    public Company? GetWithGraphRelations(long id) => throw new NotSupportedException();
    public Company? GetWithGraphRelationsByCik(string cik) => throw new NotSupportedException();
    public IEnumerable<Company> Search(string? term) => throw new NotSupportedException();
    public IEnumerable<Company> Lookup(string? term) => throw new NotSupportedException();
    public void Add(Company entity) => throw new NotSupportedException();
    public void Update(Company entity) => throw new NotSupportedException();
    public void SoftDelete(long id) => throw new NotSupportedException();
    public void ReplaceFinancials(long companyId, IReadOnlyList<CompanyFinancial> rows) => throw new NotSupportedException();
    public HashSet<long> CompanyIdsWithFinancials() => throw new NotSupportedException();
    public IReadOnlyDictionary<string, long> CikToIdMap() => throw new NotSupportedException();
    public IReadOnlyDictionary<long, double?> MarketCapsByIds(IEnumerable<long> ids) => throw new NotSupportedException();
    public IReadOnlyDictionary<long, simple_bloomberg_terminal.Models.Enums.Sector> SectorsByIds(IEnumerable<long> ids) => throw new NotSupportedException();
}
