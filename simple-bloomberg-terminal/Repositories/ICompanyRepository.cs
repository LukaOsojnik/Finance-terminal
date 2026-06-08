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
}
