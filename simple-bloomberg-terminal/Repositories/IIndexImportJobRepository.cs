using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

/// <summary>
/// Persists index-import jobs so they survive a restart and are visible to every user (not just the
/// browser that started one). Backs the "continue a partial import under your own FMP key" flow on the
/// Indices page.
/// </summary>
public interface IIndexImportJobRepository
{
    void Add(IndexImportJob job);
    IndexImportJob? Get(long id);
    void Update(IndexImportJob job);

    // Most-recent-first, for the jobs list on the Indices page.
    IReadOnlyList<IndexImportJob> Recent(int take = 20);
}
