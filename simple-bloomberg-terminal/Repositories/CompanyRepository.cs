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

    // AsSplitQuery: five collection Includes off Company would otherwise be one JOINed query whose
    // row count is the *product* of the collections (cartesian explosion) — crippling for a company
    // with many events. Split fetches each collection separately. Events is filtered to active so
    // soft-deleted rows (e.g. retired filing-events) aren't loaded only to be discarded in memory.
    public Company? GetWithGraphRelations(long id) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events.Where(e => e.DeletedAt == null))
            .Include(c => c.RevenueSources).ThenInclude(r => r.RelatedCompany)
            .Include(c => c.RevenueSources).ThenInclude(r => r.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.RelatedCompany)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.CompanyRisks).ThenInclude(r => r.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.RevenueFromDependents).ThenInclude(r => r.Company)
            .Include(c => c.CostFromDependents).ThenInclude(c2 => c2.Company)
            .Include(c => c.Financials.Where(f => f.DeletedAt == null))
            .AsSplitQuery()
            .FirstOrDefault(c => c.Id == id && c.DeletedAt == null);

    // Exact, literal CIK match (caller sends the format stored in the DB). Same includes as
    // GetWithGraphRelations so the graph converter has every relation it needs.
    public Company? GetWithGraphRelationsByCik(string cik) =>
        db.Companies
            .Include(c => c.Country)
            .Include(c => c.Events.Where(e => e.DeletedAt == null))
            .Include(c => c.RevenueSources).ThenInclude(r => r.RelatedCompany)
            .Include(c => c.RevenueSources).ThenInclude(r => r.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.RelatedCompany)
            .Include(c => c.CostSources).ThenInclude(c2 => c2.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.CompanyRisks).ThenInclude(r => r.Reviews).ThenInclude(rv => rv.Filing)
            .Include(c => c.RevenueFromDependents).ThenInclude(r => r.Company)
            .Include(c => c.CostFromDependents).ThenInclude(c2 => c2.Company)
            .AsSplitQuery()
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

    public Company? MatchByName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        var norm = NormalizeName(name);
        if (norm.Length == 0) return null;
        // Small dataset: normalise every active company's name in memory and compare. Stripping the
        // legal suffix on both sides is what lets sonar's "NVIDIA Corporation" find DB "NVIDIA".
        return db.Companies.Where(c => c.DeletedAt == null).AsEnumerable()
            .FirstOrDefault(c => NormalizeName(c.Name) == norm);
    }

    public Company? MatchByCik(string? cik)
    {
        var norm = Cik.Normalize(cik);
        if (norm is null) return null;
        return db.Companies.Where(c => c.DeletedAt == null && c.Cik != null).AsEnumerable()
            .FirstOrDefault(c => Cik.Normalize(c.Cik) == norm);
    }

    public IReadOnlyList<(string Name, string Cik)> BackfillUsCiksByName(IEnumerable<(string Title, string Cik)> secEntries)
    {
        // Normalized SEC title -> CIK, dropping any normalized name that resolves to several CIKs: an
        // ambiguous match could write the wrong filer's CIK, which is worse than leaving it null.
        var byName = new Dictionary<string, string?>();
        foreach (var (title, cik) in secEntries)
        {
            var key = NormalizeName(title);
            if (key.Length == 0) continue;
            byName[key] = byName.TryGetValue(key, out var existing) && existing != cik ? null : cik;
        }

        var filled = new List<(string, string)>();
        var usMissing = db.Companies
            .Include(c => c.Country)
            .Where(c => c.DeletedAt == null && c.Country != null && c.Country.Code == "US")
            .AsEnumerable()
            .Where(c => Cik.Normalize(c.Cik) is not { } d || d.Trim('0').Length == 0);  // null or 0000000000

        foreach (var c in usMissing)
            if (byName.TryGetValue(NormalizeName(c.Name), out var cik) && cik is not null)
            {
                c.Cik = cik;
                filled.Add((c.Name, cik));
            }

        if (filled.Count > 0) db.SaveChanges();
        return filled;
    }

    // Lowercase, drop punctuation, then drop common corporate-suffix tokens so two spellings of the
    // same company collapse to one key. "The Coca-Cola Company" -> "coca cola"; "NVIDIA Corp" -> "nvidia".
    private static readonly HashSet<string> NameNoise = new(StringComparer.Ordinal)
    {
        "inc", "incorporated", "corp", "corporation", "co", "company", "ltd", "limited", "plc", "llc",
        "lp", "llp", "ag", "sa", "nv", "se", "ab", "oyj", "as", "spa", "gmbh", "bv", "pte", "kk",
        "group", "holdings", "holding", "the"
    };

    private static string NormalizeName(string s)
    {
        var cleaned = new string(s.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : ' ').ToArray());
        var tokens = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(t => !NameNoise.Contains(t));
        return string.Join(' ', tokens);
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

    public void ReplaceFinancials(long companyId, IReadOnlyList<CompanyFinancial> rows)
    {
        if (rows.Count == 0) return;
        db.CompanyFinancials.RemoveRange(db.CompanyFinancials.Where(f => f.CompanyId == companyId));
        db.CompanyFinancials.AddRange(rows);
        db.SaveChanges();
    }

    public IReadOnlyList<CompanyVolumeHistory> GetVolumeHistory(long companyId) =>
        db.CompanyVolumeHistories
            .Where(v => v.CompanyId == companyId)
            .OrderBy(v => v.WeekStart)
            .ToList();

    public void ReplaceVolumeHistory(long companyId, IReadOnlyList<CompanyVolumeHistory> rows)
    {
        if (rows.Count == 0) return;
        db.CompanyVolumeHistories.RemoveRange(db.CompanyVolumeHistories.Where(v => v.CompanyId == companyId));
        db.CompanyVolumeHistories.AddRange(rows);
        db.SaveChanges();
    }

    public IReadOnlyDictionary<string, long> CikToIdMap()
    {
        var map = new Dictionary<string, long>();
        var rows = db.Companies
            .Where(c => c.DeletedAt == null && c.Cik != null)
            .Select(c => new { c.Id, c.Cik })
            .ToList();
        foreach (var r in rows)
        {
            var norm = Cik.Normalize(r.Cik);
            if (norm != null) map.TryAdd(norm, r.Id);  // first company per CIK wins
        }
        return map;
    }

    public IReadOnlyDictionary<long, double?> MarketCapsByIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        return db.Companies
            .Where(c => idList.Contains(c.Id) && c.DeletedAt == null)
            .Select(c => new { c.Id, c.MarketCap })
            .ToDictionary(x => x.Id, x => x.MarketCap);
    }

    public IReadOnlyDictionary<long, Models.Enums.Sector> SectorsByIds(IEnumerable<long> ids)
    {
        var idList = ids.ToList();
        // Unclassified (null-sector) companies are excluded — they can't contribute to a sector mix.
        return db.Companies
            .Where(c => idList.Contains(c.Id) && c.DeletedAt == null && c.Sector != null)
            .Select(c => new { c.Id, Sector = c.Sector!.Value })
            .ToDictionary(x => x.Id, x => x.Sector);
    }

    public HashSet<long> CompanyIdsWithFinancials() =>
        db.CompanyFinancials.Where(f => f.DeletedAt == null)
            .Select(f => f.CompanyId).Distinct().ToHashSet();

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
