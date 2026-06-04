using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

public class FilingExtractionService : IFilingExtractionService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IDeepSeekClient _llm;
    private readonly IMemoryCache _cache;
    private readonly string _model;

    private const int MaxParallel = 6;   // concurrent Flash workers in the map phase

    public FilingExtractionService(
        ICompanyRepository companies, IStockApiClient client, IDeepSeekClient llm,
        IMemoryCache cache, IConfiguration config)
    {
        _companies = companies;
        _client = client;
        _llm = llm;
        _cache = cache;
        _model = config["DeepSeek:ScanModel"] ?? "deepseek-v4-flash";
    }

    // The chat grounds on this key; both the auto-scan and a curated heading scan write it.
    public static string FindingsKey(string accession, string doc) => $"filing-findings:{accession}:{doc}";
    private static string HeadingsKey(string accession, string doc) => $"filing-headings:{accession}:{doc}";
    private static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(30);

    private const string System =
        "You extract revenue sources for a single US public company from one excerpt of its SEC " +
        "filing. Return ONLY the sources clearly evidenced in THIS excerpt — do not guess or carry " +
        "over outside knowledge. For each source provide: name (the segment / product / region / " +
        "major-customer label), classification (exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT), " +
        "value (revenue in absolute US dollars — scale any 'in thousands/millions' to the full " +
        "number; null if not stated), percentage (share of total revenue 0-100, null if not stated), " +
        "related_company (a named counterparty/customer if the row is about one, else null). For " +
        "every field you fill, include in 'proof' the VERBATIM substring of this excerpt that backs " +
        "it (null for fields you left null). Reply with JSON only, no prose, no code fences: " +
        "{\"sources\":[{\"name\":\"\",\"classification\":\"\",\"value\":null,\"percentage\":null," +
        "\"related_company\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
        "\"classification\":null,\"related_company\":null}}]}. If the excerpt names no revenue " +
        "source, reply {\"sources\":[]}.";

    public async Task<IReadOnlyList<ExtractionSuggestion>> ExtractAsync(
        long companyId, string accession, string doc, CancellationToken ct = default)
    {
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        if (raw is null) return [];
        return await ScanChunksAsync(FilingSections.Build(raw), ct);
    }

    // The chat's grounding digest: cached per filing; built by the all-sections auto-scan on a miss.
    // A curated heading scan (ScanSelectedHeadingsAsync) overwrites this key with its own digest.
    public async Task<string> GetOrScanDigestAsync(
        long companyId, string accession, string doc, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(FindingsKey(accession, doc), out string? cached)) return cached ?? "";
        var findings = await ExtractAsync(companyId, accession, doc, ct);
        var digest = findings.Count > 0 ? FormatDigest(findings) : "";
        _cache.Set(FindingsKey(accession, doc), digest, CacheFor);
        return digest;
    }

    // The bold sub-headings the user picks from. The parsed list is cached so a later scan can map
    // the selected ids (its indices) back to the paragraphs to read.
    public async Task<IReadOnlyList<HeadingInfo>> GetHeadingsAsync(
        long companyId, string accession, string doc, CancellationToken ct = default)
    {
        var headings = await GetOrParseHeadingsAsync(companyId, accession, doc, ct);
        return headings
            .Select((h, i) => new HeadingInfo(i, h.Title, h.Section, h.Body.Length))
            .ToList();
    }

    // Spawn one worker per selected heading, read only those paragraphs, and overwrite the chat's
    // grounding digest with the curated result. Returns how many candidates were found.
    public async Task<int> ScanSelectedHeadingsAsync(
        long companyId, string accession, string doc, IReadOnlyList<int> headingIds, CancellationToken ct = default)
    {
        var headings = await GetOrParseHeadingsAsync(companyId, accession, doc, ct);
        var chunks = headingIds
            .Where(i => i >= 0 && i < headings.Count)
            .Select(i => new FilingChunk($"{headings[i].Section} › {headings[i].Title}", headings[i].Body))
            .ToList();
        if (chunks.Count == 0) return 0;

        var findings = await ScanChunksAsync(chunks, ct);
        var digest = findings.Count > 0 ? FormatDigest(findings) : "";
        _cache.Set(FindingsKey(accession, doc), digest, CacheFor);
        return findings.Count;
    }

    private async Task<List<FilingHeading>> GetOrParseHeadingsAsync(
        long companyId, string accession, string doc, CancellationToken ct)
    {
        if (_cache.TryGetValue(HeadingsKey(accession, doc), out List<FilingHeading>? cached) && cached is not null)
            return cached;
        var raw = await FetchRawAsync(companyId, accession, doc, ct);
        var headings = raw is null ? [] : FilingSections.BuildHeadings(raw);
        _cache.Set(HeadingsKey(accession, doc), headings, CacheFor);
        return headings;
    }

    private async Task<string?> FetchRawAsync(long companyId, string accession, string doc, CancellationToken ct)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return null;
        var raw = await _client.GetFilingDocument(company.Cik.TrimStart('0'), accession.Replace("-", ""), doc);
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }

    // Map/reduce over a given set of chunks: parallel Flash workers, then dedupe by name (first wins).
    private async Task<List<ExtractionSuggestion>> ScanChunksAsync(
        IReadOnlyList<FilingChunk> chunks, CancellationToken ct)
    {
        using var gate = new SemaphoreSlim(MaxParallel);
        var perChunk = await Task.WhenAll(chunks.Select(c => ScanChunkAsync(c, gate, ct)));

        var byName = new Dictionary<string, ExtractionSuggestion>(StringComparer.OrdinalIgnoreCase);
        foreach (var list in perChunk)
            foreach (var s in list)
                if (!string.IsNullOrWhiteSpace(s.Name) && !byName.ContainsKey(s.Name))
                    byName[s.Name] = s;
        return byName.Values.ToList();
    }

    // Compact, model-readable digest of the workers' candidates (names/values + verbatim proof), so
    // the Pro chat agent can cite them and fill ```save``` blocks without re-reading the filing.
    private static string FormatDigest(IReadOnlyList<ExtractionSuggestion> findings)
    {
        var sb = new StringBuilder(
            "PARALLEL-SCAN FINDINGS (revenue/cost candidates the worker agents pulled from the filing):\n");
        foreach (var s in findings)
        {
            sb.Append("- ").Append(s.Name);
            if (s.Classification != null) sb.Append(" [").Append(s.Classification).Append(']');
            if (s.Value != null) sb.Append(" | value=").Append(s.Value);
            if (s.Percentage != null) sb.Append(" | pct=").Append(s.Percentage);
            if (!string.IsNullOrWhiteSpace(s.RelatedCompany)) sb.Append(" | counterparty=").Append(s.RelatedCompany);
            sb.Append(" | from ").Append(s.Section).Append('\n');
            AppendProof(sb, "name", s.Proof.Name);
            AppendProof(sb, "value", s.Proof.Value);
            AppendProof(sb, "percentage", s.Proof.Percentage);
            AppendProof(sb, "classification", s.Proof.Classification);
            AppendProof(sb, "related_company", s.Proof.RelatedCompany);
        }
        return sb.ToString();
    }

    private static void AppendProof(StringBuilder sb, string field, string? proof)
    {
        if (!string.IsNullOrWhiteSpace(proof))
            sb.Append("    proof.").Append(field).Append(": \"").Append(proof).Append("\"\n");
    }

    // One worker: read a single chunk under the concurrency gate and return its candidates.
    private async Task<List<ExtractionSuggestion>> ScanChunkAsync(
        FilingChunk chunk, SemaphoreSlim gate, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            var prompt = $"Section: {chunk.Section}\n\nExcerpt:\n\"\"\"\n{chunk.Text}\n\"\"\"";
            var answer = await _llm.CompleteAsync(_model, System, prompt, maxTokens: 1500, jsonObject: true, ct: ct);
            return Parse(answer, chunk.Section).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return [];   // a dropped worker shouldn't sink the whole scan
        }
        finally { gate.Release(); }
    }

    // Pull suggestions out of the model's JSON, tolerant of code fences and string-or-number values.
    private static IEnumerable<ExtractionSuggestion> Parse(string answer, string section)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) yield break;

        JsonDocument doc;
        try { doc = JsonDocument.Parse(answer[start..(end + 1)]); }
        catch (JsonException) { yield break; }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("sources", out var sources) ||
                sources.ValueKind != JsonValueKind.Array) yield break;

            foreach (var el in sources.EnumerateArray())
            {
                var name = Str(el, "name");
                if (string.IsNullOrWhiteSpace(name)) continue;
                var proof = el.TryGetProperty("proof", out var p) ? p : default;
                yield return new ExtractionSuggestion(
                    Name: name!,
                    Classification: Str(el, "classification"),
                    Value: Num(el, "value"),
                    Percentage: Num(el, "percentage"),
                    RelatedCompany: Str(el, "related_company"),
                    Section: section,
                    Proof: new ExtractionProof(
                        Str(proof, "name"), Str(proof, "value"), Str(proof, "percentage"),
                        Str(proof, "classification"), Str(proof, "related_company")));
            }
        }
    }

    private static string? Str(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.ToString(),
            _ => null
        };
    }

    private static double? Num(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return n;
        if (v.ValueKind == JsonValueKind.String &&
            double.TryParse(v.GetString()?.Replace(",", "").Replace("$", "").Replace("%", ""),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var s)) return s;
        return null;
    }
}
