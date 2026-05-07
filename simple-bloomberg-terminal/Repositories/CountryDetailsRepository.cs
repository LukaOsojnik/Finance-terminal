using Microsoft.EntityFrameworkCore;
using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Repositories;

public class CountryDetailsRepository(AppDbContext db) : ICountryDetailsRepository
{
    public CountryDetails? GetByCountryId(long countryId) =>
        db.CountryDetails
            .Include(d => d.Advantages)
            .Include(d => d.Challenges)
            .Include(d => d.GdpHistory)
            .FirstOrDefault(d => d.CountryId == countryId);
}
