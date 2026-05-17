using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface IGdpSnapshotRepository
{
    IEnumerable<GdpSnapshot> GetAll();
    GdpSnapshot? GetById(long id);
    IEnumerable<GdpSnapshot> Search(string? term);
    void Add(GdpSnapshot entity);
    void Update(GdpSnapshot entity);
    void SoftDelete(long id);
}
