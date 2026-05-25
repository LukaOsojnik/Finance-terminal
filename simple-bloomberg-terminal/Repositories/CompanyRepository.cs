using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CompanyRepository(AppDbContext db) : ICompanyRepository
{
    public IEnumerable<Company> GetAll() =>
        db.Companies
            .Include(c => c.Country)
            .Where(c => c.DeletedAt == null)
            .OrderBy(c => c.Name)
            .ToList();

    public Company? GetById(long id) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events)
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    public Company? GetWithGraphRelations(long id) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events)
            .Include(c => c.RevenueSources).ThenInclude(r => r.RelatedCompany)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.RelatedCompany)
            .Include(c => c.RevenueFromDependents).ThenInclude(r => r.Company)
            .Include(c => c.CostFromDependents).ThenInclude(c2 => c2.Company)
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    // Exact, literal CIK match (caller sends the format stored in the DB). Same includes as
    // GetWithGraphRelations so the graph converter has every relation it needs.
    public Company? GetWithGraphRelationsByCik(string cik) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events)
            .Include(c => c.RevenueSources).ThenInclude(r => r.RelatedCompany)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.RelatedCompany)
            .Include(c => c.RevenueFromDependents).ThenInclude(r => r.Company)
            .Include(c => c.CostFromDependents).ThenInclude(c2 => c2.Company)
            .FirstOrDefault(c => c.Cik == cik && c.DeletedAt == null);

    public IEnumerable<Company> Search(string? term)
    {
        var q = db.Companies
            .Include(c => c.Country)
            .Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(c =>
                EF.Functions.Like(c.Name, $"{t}%") ||
                (c.Cik != null && EF.Functions.Like(c.Cik, $"{t}%")) ||
                (c.Country != null && EF.Functions.Like(c.Country.Name, $"{t}%")));
        }
        return q.OrderBy(c => c.Name).ToList();
    }

    public IEnumerable<Company> Lookup(string? term)
    {
        var q = db.Companies.Where(c => c.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(c =>
                EF.Functions.Like(c.Name, $"{t}%") ||
                (c.Cik != null && EF.Functions.Like(c.Cik, $"{t}%")));
        }
        return q.OrderBy(c => c.Name).ToList();
    }

    public void Add(Company entity)
    {
        db.Companies.Add(entity);
        db.SaveChanges();
    }

    public void Update(Company entity)
    {
        db.Companies.Update(entity);
        db.SaveChanges();
    }

    public void SoftDelete(long id)
    {
        var entity = db.Companies.FirstOrDefault(c => c.Id == id);
        if (entity == null || entity.DeletedAt != null) return;

        var hasActiveRevenue = db.RevenueSources.Any(x => x.CompanyId == id && x.DeletedAt == null);
        var hasActiveCost = db.CostSources.Any(x => x.CompanyId == id && x.DeletedAt == null);
        if (hasActiveRevenue || hasActiveCost)
            throw new InvalidOperationException("Cannot delete company: active revenue or cost sources exist.");

        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
