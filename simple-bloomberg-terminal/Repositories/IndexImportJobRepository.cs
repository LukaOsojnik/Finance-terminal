using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class IndexImportJobRepository(AppDbContext db) : IIndexImportJobRepository
{
    public void Add(IndexImportJob job)
    {
        db.IndexImportJobs.Add(job);
        db.SaveChanges();
    }

    public IndexImportJob? Get(long id) =>
        db.IndexImportJobs.FirstOrDefault(j => j.Id == id);

    public void Update(IndexImportJob job)
    {
        db.IndexImportJobs.Update(job);
        db.SaveChanges();
    }

    public IReadOnlyList<IndexImportJob> Recent(int take = 20) =>
        db.IndexImportJobs
            .OrderByDescending(j => j.CreatedAt)
            .Take(take)
            .ToList();
}
