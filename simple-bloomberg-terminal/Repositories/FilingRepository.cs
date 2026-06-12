using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public class FilingRepository(AppDbContext db) : IFilingRepository
{
    public IEnumerable<Filing> GetAll() =>
        db.Filings
            .Include(f => f.Company)
            .Where(f => f.DeletedAt == null)
            .OrderByDescending(f => f.FilingDate)
            .ToList();

    // Accession is globally unique and the unique index spans soft-deleted rows too, so this
    // upsert lookup must find a row regardless of DeletedAt — the caller revives it rather than
    // inserting a duplicate.
    public Filing? GetByAccession(string accessionNumber) =>
        db.Filings.FirstOrDefault(f => f.AccessionNumber == accessionNumber);

    public Filing? GetById(long id) =>
        db.Filings.FirstOrDefault(f => f.Id == id && f.DeletedAt == null);

    public IEnumerable<Filing> GetByCompany(long companyId) =>
        db.Filings
            .Where(f => f.CompanyId == companyId && f.DeletedAt == null)
            .OrderByDescending(f => f.FilingDate)
            .ToList();

    public IEnumerable<Filing> Search(string? term)
    {
        var q = db.Filings.Include(f => f.Company).Where(f => f.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(f =>
                EF.Functions.Like(f.AccessionNumber, $"%{t}%") ||
                (f.Form != null && EF.Functions.Like(f.Form, $"%{t}%")) ||
                (f.Company != null && EF.Functions.Like(f.Company.Name, $"%{t}%")));
        }
        return q.OrderByDescending(f => f.FilingDate).ToList();
    }

    public void Add(Filing entity) { db.Filings.Add(entity); db.SaveChanges(); }
    public void Update(Filing entity) { db.Filings.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.Filings.FirstOrDefault(f => f.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }

    public Filing Upsert(long companyId, string accessionNumber, string? form, DateTime? filingDate, string? primaryDocUrl)
    {
        // GetByAccession ignores soft-delete, so a previously-deleted filing is revived here rather
        // than re-inserted (which would collide with the unique accession index).
        var existing = GetByAccession(accessionNumber);
        if (existing is not null)
        {
            existing.DeletedAt = null;
            if (form is not null) existing.Form = form;
            if (filingDate is not null) existing.FilingDate = filingDate;
            if (primaryDocUrl is not null) existing.PrimaryDocUrl = primaryDocUrl;
            db.SaveChanges();
            return existing;
        }

        var filing = new Filing
        {
            CompanyId = companyId,
            AccessionNumber = accessionNumber,
            Form = form,
            FilingDate = filingDate,
            PrimaryDocUrl = primaryDocUrl
        };
        db.Filings.Add(filing);
        db.SaveChanges();
        return filing;
    }

    public void SoftDeleteSourceCluster(RelationKind relation, long sourceId)
    {
        var now = DateTime.UtcNow;

        // Filings this source cites, via its per-field reviews (a source can cite several).
        var filingIds = db.SourceFieldReviews
            .Where(r => r.DeletedAt == null && r.FilingId != null &&
                (relation == RelationKind.REVENUE ? r.RevenueSourceId == sourceId : r.CostSourceId == sourceId))
            .Select(r => r.FilingId!.Value)
            .Distinct()
            .ToList();

        if (filingIds.Count == 0)
        {
            // No proof filing — remove just this source and its own reviews.
            SoftDeleteSourcesAndReviews(now,
                relation == RelationKind.REVENUE ? new List<long> { sourceId } : new List<long>(),
                relation == RelationKind.COST ? new List<long> { sourceId } : new List<long>());
            return;
        }

        // Cluster: every source that cites any of those filings (via its reviews).
        var clusterReviews = db.SourceFieldReviews
            .Where(r => r.DeletedAt == null && r.FilingId != null && filingIds.Contains(r.FilingId.Value))
            .ToList();
        var revIds = clusterReviews.Where(r => r.RevenueSourceId != null).Select(r => r.RevenueSourceId!.Value).Distinct().ToList();
        var costIds = clusterReviews.Where(r => r.CostSourceId != null).Select(r => r.CostSourceId!.Value).Distinct().ToList();

        SoftDeleteSourcesAndReviews(now, revIds, costIds);

        // Soft-delete the filings themselves.
        foreach (var f in db.Filings.Where(f => filingIds.Contains(f.Id) && f.DeletedAt == null))
            f.DeletedAt = now;

        db.SaveChanges();
    }

    // Soft-delete the given source rows plus ALL of their reviews (every cell, not only the ones
    // that cited a filing). Caller commits.
    private void SoftDeleteSourcesAndReviews(DateTime now, List<long> revIds, List<long> costIds)
    {
        foreach (var r in db.RevenueSources.Where(s => revIds.Contains(s.Id) && s.DeletedAt == null))
            r.DeletedAt = now;
        foreach (var c in db.CostSources.Where(s => costIds.Contains(s.Id) && s.DeletedAt == null))
            c.DeletedAt = now;
        foreach (var rv in db.SourceFieldReviews.Where(r => r.DeletedAt == null &&
                     ((r.RevenueSourceId != null && revIds.Contains(r.RevenueSourceId.Value)) ||
                      (r.CostSourceId != null && costIds.Contains(r.CostSourceId.Value)))))
            rv.DeletedAt = now;

        db.SaveChanges();
    }
}
