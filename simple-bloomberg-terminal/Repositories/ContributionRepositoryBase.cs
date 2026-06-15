using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

/// <summary>
/// Shared CRUD + soft-delete + Approved/Pending filtering for the three contribution
/// repositories (revenue, cost, risk). Subclasses supply only their <see cref="Set"/> and
/// the eager-load (Include) shape for each read; the filtering and ordering invariants live
/// here so the soft-delete + <c>Status == Approved</c> rule cannot drift between repos.
/// </summary>
public abstract class ContributionRepositoryBase<T>(AppDbContext db) where T : class, IContribution
{
    protected readonly AppDbContext Db = db;

    protected abstract DbSet<T> Set { get; }

    // Eager-load shapes — these differ per entity (cost/revenue also pull RelatedCompany),
    // so each is concrete in the subclass where the navigations are strongly typed.
    protected abstract IQueryable<T> ListIncludes(IQueryable<T> q);             // GetAll, Search
    protected abstract IQueryable<T> DetailIncludes(IQueryable<T> q);           // GetById
    protected abstract IQueryable<T> PendingFeedIncludes(IQueryable<T> q);      // GetAllPending (light)
    protected abstract IQueryable<T> PendingByCompanyIncludes(IQueryable<T> q); // GetPendingByCompany

    public IEnumerable<T> GetAll() =>
        ListIncludes(Set)
            .Where(e => e.DeletedAt == null && e.Status == ContributionStatus.Approved)
            .OrderBy(e => e.Company!.Name).ThenBy(e => e.Name)
            .ToList();

    // Manager review feed: every pending contribution across all companies (light — Company only).
    public IEnumerable<T> GetAllPending() =>
        PendingFeedIncludes(Set)
            .Where(e => e.DeletedAt == null && e.Status == ContributionStatus.Pending)
            .OrderBy(e => e.Company!.Name).ThenBy(e => e.Name)
            .ToList();

    // Pending contributions for one company's review page.
    public IEnumerable<T> GetPendingByCompany(long companyId) =>
        PendingByCompanyIncludes(Set)
            .Where(e => e.CompanyId == companyId && e.DeletedAt == null && e.Status == ContributionStatus.Pending)
            .OrderBy(e => e.Name)
            .ToList();

    public T? GetById(long id) =>
        DetailIncludes(Set)
            .FirstOrDefault(e => e.Id == id && e.DeletedAt == null);

    public IEnumerable<T> Search(string? term)
    {
        var q = ListIncludes(Set)
            .Where(e => e.DeletedAt == null && e.Status == ContributionStatus.Approved);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(e =>
                EF.Functions.Like(e.Name, $"%{t}%") ||
                (e.Company != null && EF.Functions.Like(e.Company.Name, $"%{t}%")));
        }
        return q.OrderBy(e => e.Company!.Name).ThenBy(e => e.Name).ToList();
    }

    public void Add(T entity) { Set.Add(entity); Db.SaveChanges(); }
    public void Update(T entity) { Set.Update(entity); Db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = Set.FirstOrDefault(e => e.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        Db.SaveChanges();
    }

    // Idempotent external-refresh support: soft-delete a company's rows from one source.
    public void ClearByCompanyAndDataSource(long companyId, DataSource source)
    {
        var rows = Set
            .Where(e => e.CompanyId == companyId && e.DataSource == source && e.DeletedAt == null)
            .ToList();
        if (rows.Count == 0) return;
        foreach (var e in rows) e.DeletedAt = DateTime.UtcNow;
        Db.SaveChanges();
    }
}
