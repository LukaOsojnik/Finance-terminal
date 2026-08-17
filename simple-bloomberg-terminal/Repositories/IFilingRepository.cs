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
    /// itself, the filing it cites, and every other source citing that same filing. When the source
    /// cites no filing, only the source is removed. One transaction.
    /// </summary>
    void SoftDeleteSourceCluster(ExtractionNode node, long sourceId);
}
