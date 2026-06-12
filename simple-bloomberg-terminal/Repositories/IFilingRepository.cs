using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public interface IFilingRepository
{
    IEnumerable<Filing> GetAll();
    Filing? GetByAccession(string accessionNumber);
    Filing? GetById(long id);
    IEnumerable<Filing> GetByCompany(long companyId);
    IEnumerable<Filing> Search(string? term);
    void Add(Filing entity);
    void Update(Filing entity);
    void SoftDelete(long id);

    /// <summary>
    /// Find-or-create a Filing by its (globally unique) accession number, reviving a soft-deleted
    /// row rather than inserting a duplicate. Refreshes form/date/url metadata. Returns the row.
    /// </summary>
    Filing Upsert(long companyId, string accessionNumber, string? form, DateTime? filingDate, string? primaryDocUrl);

    /// <summary>
    /// Soft-delete a cost/revenue source and the whole filing cluster connected to it: the source
    /// itself, its proof reviews, the filing it links to, and every other source that links to
    /// that same filing (with their reviews). When the source has no filing, only the source and
    /// its own reviews are removed. One transaction.
    /// </summary>
    void SoftDeleteSourceCluster(RelationKind relation, long sourceId);
}
