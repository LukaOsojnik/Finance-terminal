using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CompanyRepository(AppDbContext db) : ICompanyRepository
{
    public IEnumerable<Company> GetAll() => db.Companies.Include(c => c.Country).ToList();

    public Company? GetById(long id) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events)
            .FirstOrDefault(c => c.Id == id);
}
