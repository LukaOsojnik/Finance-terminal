using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryRepository(AppDbContext db) : ICountryRepository
{
    public IEnumerable<Country> GetAll() => db.Countries.ToList();

    public Country? GetById(long id) =>
        db.Countries
            .Include(c => c.TradeBlocs)
            .FirstOrDefault(c => c.Id == id);
}
