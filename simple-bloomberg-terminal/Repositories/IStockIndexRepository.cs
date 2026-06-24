using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IStockIndexRepository
{
    IEnumerable<StockIndex> GetAll();

    // One index with its constituents and each constituent's Company (incl. Sector) loaded — the
    // shape the sector/industry breakdown view needs. Null if missing or soft-deleted.
    StockIndex? GetWithConstituents(long id);

    // Find an existing index by its FMP code (e.g. "nasdaq"), or null. Lets the importer upsert
    // instead of duplicating an index on a re-import.
    StockIndex? GetByCode(string code);

    void Add(StockIndex entity);

    // Replace this index's membership with a freshly-imported set (clear-reinsert, like
    // ReplaceFinancials) and stamp AsOf. Junction rows are pure derived data, so hard-deleted.
    void ReplaceConstituents(long indexId, IReadOnlyList<IndexConstituent> rows, DateOnly asOf);

    void SoftDelete(long id);
}
