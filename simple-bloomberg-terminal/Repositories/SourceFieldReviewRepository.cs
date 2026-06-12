using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class SourceFieldReviewRepository(AppDbContext db) : ISourceFieldReviewRepository
{
    public IEnumerable<SourceFieldReview> GetAll() =>
        db.SourceFieldReviews
            .Include(r => r.Company)
            .Include(r => r.RevenueSource)
            .Include(r => r.CostSource)
            .Include(r => r.CompanyRisk)
            .Include(r => r.Filing)
            .Where(r => r.DeletedAt == null)
            .OrderByDescending(r => r.Id)
            .ToList();

    public IEnumerable<SourceFieldReview> Search(string? term)
    {
        var q = db.SourceFieldReviews
            .Include(r => r.Company)
            .Include(r => r.RevenueSource)
            .Include(r => r.CostSource)
            .Include(r => r.CompanyRisk)
            .Include(r => r.Filing)
            .Where(r => r.DeletedAt == null);
        if (!string.IsNullOrWhiteSpace(term))
        {
            var t = term.Trim();
            q = q.Where(r =>
                EF.Functions.Like(r.Endpoint, $"%{t}%") ||
                (r.Rationale != null && EF.Functions.Like(r.Rationale, $"%{t}%")) ||
                (r.Company != null && EF.Functions.Like(r.Company.Name, $"%{t}%")));
        }
        return q.OrderByDescending(r => r.Id).ToList();
    }

    public IEnumerable<SourceFieldReview> GetByCompany(long companyId) =>
        db.SourceFieldReviews
            .Include(r => r.Company)
            .Include(r => r.RevenueSource)
            .Include(r => r.CostSource)
            .Include(r => r.CompanyRisk)
            .Include(r => r.Filing)
            .Where(r => r.CompanyId == companyId && r.DeletedAt == null)
            .ToList();

    public IEnumerable<SourceFieldReview> GetUnreviewed() =>
        db.SourceFieldReviews
            .Include(r => r.Company)
            .Include(r => r.RevenueSource)
            .Include(r => r.CostSource)
            .Include(r => r.CompanyRisk)
            .Include(r => r.Filing)
            .Where(r => r.Mark == null && r.DeletedAt == null)
            .ToList();

    public SourceFieldReview? GetById(long id) =>
        db.SourceFieldReviews
            .Include(r => r.Company)
            .Include(r => r.RevenueSource)
            .Include(r => r.CostSource)
            .Include(r => r.CompanyRisk)
            .Include(r => r.Filing)
            .FirstOrDefault(r => r.Id == id && r.DeletedAt == null);

    public void Add(SourceFieldReview entity) { db.SourceFieldReviews.Add(entity); db.SaveChanges(); }
    public void Update(SourceFieldReview entity) { db.SourceFieldReviews.Update(entity); db.SaveChanges(); }

    public void SoftDelete(long id)
    {
        var entity = db.SourceFieldReviews.FirstOrDefault(r => r.Id == id);
        if (entity == null || entity.DeletedAt != null) return;
        entity.DeletedAt = DateTime.UtcNow;
        db.SaveChanges();
    }
}
