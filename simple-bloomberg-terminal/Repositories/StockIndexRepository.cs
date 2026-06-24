using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class StockIndexRepository(AppDbContext db) : IStockIndexRepository
{
    public IEnumerable<StockIndex> GetAll() =>
        db.StockIndices
            .Include(i => i.Constituents)
            .Where(i => i.DeletedAt == null)
            .OrderBy(i => i.Name)
            .ToList();

    public StockIndex? GetWithConstituents(long id) =>
        db.StockIndices
            .Include(i => i.Constituents.OrderByDescending(c => c.WeightPct))
                .ThenInclude(c => c.Company)
            .FirstOrDefault(i => i.Id == id && i.DeletedAt == null);

    public StockIndex? GetByCode(string code) =>
        db.StockIndices.FirstOrDefault(i => i.Code == code && i.DeletedAt == null);

    public void Add(StockIndex entity)
    {
        db.StockIndices.Add(entity);
        db.SaveChanges();
    }

    public void ReplaceConstituents(long indexId, IReadOnlyList<IndexConstituent> rows, DateOnly asOf)
    {
        db.IndexConstituents.RemoveRange(db.IndexConstituents.Where(c => c.StockIndexId == indexId));
        foreach (var r in rows) r.StockIndexId = indexId;
        db.IndexConstituents.AddRange(rows);

        var index = db.StockIndices.FirstOrDefault(i => i.Id == indexId);
        if (index != null) index.AsOf = asOf;

        db.SaveChanges();
    }

    public void SoftDelete(long id)
    {
        var entity = db.StockIndices.FirstOrDefault(i => i.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
