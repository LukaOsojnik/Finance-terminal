using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public interface ISourceFieldReviewRepository
{
    IEnumerable<SourceFieldReview> GetAll();
    IEnumerable<SourceFieldReview> GetByCompany(long companyId);
    IEnumerable<SourceFieldReview> GetUnreviewed();
    IEnumerable<SourceFieldReview> Search(string? term);
    SourceFieldReview? GetById(long id);
    void Add(SourceFieldReview entity);
    void Update(SourceFieldReview entity);
    void SoftDelete(long id);
}
