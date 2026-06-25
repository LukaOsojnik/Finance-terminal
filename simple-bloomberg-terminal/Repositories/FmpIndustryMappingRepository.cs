using simple_bloomberg_terminal.Data;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Repositories;

public class FmpIndustryMappingRepository(AppDbContext db) : IFmpIndustryMappingRepository
{
    public GicsSubIndustry? Get(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;
        var key = IFmpIndustryMappingRepository.Normalize(label);
        if (key.Length == 0) return null;
        return db.FmpIndustryMappings
            .Where(m => m.Label == key)
            .Select(m => (GicsSubIndustry?)m.SubIndustry)
            .FirstOrDefault();
    }

    public void Set(string? label, GicsSubIndustry subIndustry)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        var key = IFmpIndustryMappingRepository.Normalize(label);
        if (key.Length == 0) return;

        var existing = db.FmpIndustryMappings.FirstOrDefault(m => m.Label == key);
        if (existing is null)
            db.FmpIndustryMappings.Add(new FmpIndustryMapping(key, subIndustry));
        else
            existing.SubIndustry = subIndustry;
        db.SaveChanges();
    }

    public void Remove(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        var key = IFmpIndustryMappingRepository.Normalize(label);
        if (key.Length == 0) return;

        var existing = db.FmpIndustryMappings.FirstOrDefault(m => m.Label == key);
        if (existing is not null)
        {
            db.FmpIndustryMappings.Remove(existing);
            db.SaveChanges();
        }
    }
}
