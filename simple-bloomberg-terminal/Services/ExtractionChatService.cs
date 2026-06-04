using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

public class ExtractionChatService : IExtractionChatService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly IFilingExtractionService _scan;
    private readonly IDeepSeekClient _llm;
    private readonly IMemoryCache _cache;
    private readonly string _model;

    public ExtractionChatService(
        ICompanyRepository companies, IStockApiClient client, IFilingExtractionService scan,
        IDeepSeekClient llm, IMemoryCache cache, IConfiguration config)
    {
        _companies = companies;
        _client = client;
        _scan = scan;
        _llm = llm;
        _cache = cache;
        _model = config["DeepSeek:ChatModel"] ?? "deepseek-v4-pro";
    }


    private const int ContextBudgetChars = 60_000;   // ~15k tokens of filing text per turn (cached)

    private const string SystemPrompt =
        "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
        "filing and reported the revenue/cost candidates below, each with the VERBATIM proof text " +
        "they found. Ground every claim in those findings (or the raw excerpts, if findings are " +
        "absent); if something isn't there, say so rather than guessing. Help the user review and " +
        "decide which revenue sources and cost sources (segments, products, regions, major customers, " +
        "key suppliers) and counterparty relationships to keep. Be concise.\n\n" +
        "When the user wants to SAVE a specific source, output a fenced block exactly like:\n" +
        "```save\n{\"name\":\"\",\"classification\":\"PRODUCT\",\"value\":null,\"percentage\":null," +
        "\"related_company\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
        "\"classification\":null,\"related_company\":null}}\n```\n" +
        "classification is exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT. value is absolute US " +
        "dollars (scale any 'in thousands/millions'); percentage is 0-100; use null when not stated. " +
        "Each proof field is the VERBATIM excerpt substring backing it (null for fields you left " +
        "null). Emit one save block per source the user confirms, alongside your normal reply.";

    public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc,
        IReadOnlyList<ChatMessage> history, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // The parallel worker scan runs (once per filing) before the main agent can answer — tell the
        // user so the first turn isn't a silent wait while 36 chunks fan out.
        var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
        if (hasFiling && !_cache.TryGetValue(FilingExtractionService.FindingsKey(accession, doc), out _))
            yield return new ChatDelta("status", "Scanning the filing with parallel worker agents…");

        var grounding = await GroundingAsync(companyId, accession, doc, ct);

        var messages = new List<DeepSeekMessage> { new("system", SystemPrompt + grounding) };
        foreach (var m in history)
            messages.Add(new DeepSeekMessage(m.Role == "assistant" ? "assistant" : "user", m.Content));

        await foreach (var delta in _llm.StreamAsync(_model, messages, maxTokens: 2048, ct: ct))
            yield return delta;
    }

    // The main agent's grounding: the workers' findings digest (auto-scan, or a curated heading scan
    // the user kicked off). Falls back to raw section excerpts only if the scan returned nothing.
    private async Task<string> GroundingAsync(long companyId, string accession, string doc, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc)) return "";

        var digest = await _scan.GetOrScanDigestAsync(companyId, accession, doc, ct);
        if (!string.IsNullOrEmpty(digest)) return $"\n\n{digest}";

        var raw = await RawFallbackAsync(companyId, accession, doc, ct);
        return string.IsNullOrEmpty(raw) ? "" : $"\n\n{raw}";
    }

    // Used only when the scan yields nothing: the cleaned Item 7/8/1A excerpts, budgeted per section.
    private async Task<string> RawFallbackAsync(long companyId, string accession, string doc, CancellationToken ct)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return "";
        var raw = await _client.GetFilingDocument(company.Cik.TrimStart('0'), accession.Replace("-", ""), doc);
        if (raw is null) return "";
        var context = BuildContext(raw);
        return string.IsNullOrEmpty(context)
            ? ""
            : $"FILING EXCERPTS (Item 7 MD&A, Item 8 financial-statement notes, Item 1A risk factors):\n{context}";
    }

    private static string BuildContext(string raw)
    {
        var chunks = FilingSections.Build(raw);
        if (chunks.Count == 0) return "";

        // Split the budget evenly across the sections present, so a huge Item 1A can't crowd out
        // Items 7 & 8 (where the revenue breakdown lives). Chunks already arrive in 7 → 8 → 1A order.
        var sections = chunks.Select(c => c.Section).Distinct().ToList();
        int perSection = ContextBudgetChars / sections.Count;
        var used = sections.ToDictionary(s => s, _ => 0);

        var sb = new StringBuilder();
        foreach (var chunk in chunks)
        {
            if (used[chunk.Section] + chunk.Text.Length > perSection) continue;   // this section is full
            used[chunk.Section] += chunk.Text.Length;
            sb.Append('[').Append(chunk.Section).Append("]\n").Append(chunk.Text).Append("\n\n");
        }
        return sb.ToString();
    }
}
