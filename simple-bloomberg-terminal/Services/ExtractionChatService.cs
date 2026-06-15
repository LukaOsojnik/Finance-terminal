using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

public class ExtractionChatService : IExtractionChatService
{
    private readonly ICompanyRepository _companies;
    private readonly IStockApiClient _client;
    private readonly ISec2MdClient _sec2md;
    private readonly IFilingExtractionService _scan;
    private readonly IChatLlm _llm;
    private readonly IMemoryCache _cache;

    public ExtractionChatService(
        ICompanyRepository companies, IStockApiClient client, ISec2MdClient sec2md,
        IFilingExtractionService scan, IChatLlm llm, IMemoryCache cache)
    {
        _companies = companies;
        _client = client;
        _sec2md = sec2md;
        _scan = scan;
        _llm = llm;
        _cache = cache;
    }


    private const int ContextBudgetChars = 60_000;   // ~15k tokens of filing text per turn (cached)

    // The lead-analyst system prompt, tailored to the node being built. The save-block schema must
    // match what the page's normalizeSave() reads (revenue/cost: money fields; risk: scope + note).
    private static string SystemFor(ExtractionNode node) => node switch
    {
        ExtractionNode.COST =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the COST candidates below, each with the VERBATIM proof text they " +
            "found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. Help the user review and " +
            "decide which cost sources (cost lines, segments, key suppliers) and counterparty " +
            "relationships to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific cost, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"COGS\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"related_company_ticker\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}\n```\n" +
            "classification is exactly one of COGS, OPEX, TOTAL_COSTS. value is absolute US dollars " +
            "(scale any 'in thousands/millions'); percentage is 0-100; use null when not stated. " +
            "related_company is a named supplier/counterparty (else null); when it's a publicly traded " +
            "company you can identify, also set related_company_ticker to its stock ticker (else null) " +
            "so it can be enriched. Each " +
            "proof field is the VERBATIM excerpt substring backing it (null for fields you left null). " +
            "Emit one save block per cost the user confirms, alongside your normal reply.",

        ExtractionNode.RISK =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the RISK candidates below, each with the VERBATIM proof text they " +
            "found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. Help the user review and " +
            "decide which disclosed risks to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific risk, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"BUSINESS\",\"note\":null," +
            "\"proof\":{\"name\":\"\",\"classification\":null,\"note\":null}}\n```\n" +
            "classification is the risk scope, exactly one of MACROECONOMIC, INDUSTRY, BUSINESS, " +
            "LEGAL_REGULATORY, FINANCIAL, GENERAL. note is one or two sentences summarising the risk; " +
            "use null when not stated. Each proof field is the VERBATIM excerpt substring backing it " +
            "(null for fields you left null). Emit one save block per risk the user confirms, " +
            "alongside your normal reply.",

        _ =>
            "You are the lead financial analyst. Parallel worker agents have already scanned ONE SEC " +
            "filing and reported the revenue candidates below, each with the VERBATIM proof text " +
            "they found. Ground every claim in those findings (or the raw excerpts, if findings are " +
            "absent); if something isn't there, say so rather than guessing. Help the user review and " +
            "decide which revenue sources (segments, products, regions, major customers) and " +
            "counterparty relationships to keep. Be concise.\n\n" +
            "When the user wants to SAVE a specific source, output a fenced block exactly like:\n" +
            "```save\n{\"name\":\"\",\"classification\":\"PRODUCT\",\"value\":null,\"percentage\":null," +
            "\"related_company\":null,\"related_company_ticker\":null,\"proof\":{\"name\":\"\",\"value\":null,\"percentage\":null," +
            "\"classification\":null,\"related_company\":null}}\n```\n" +
            "classification is exactly one of CUSTOMER, SEGMENT, REGION, PRODUCT. value is absolute US " +
            "dollars (scale any 'in thousands/millions'); percentage is 0-100; use null when not stated. " +
            "related_company is a named customer/counterparty (else null); when it's a publicly traded " +
            "company you can identify, also set related_company_ticker to its stock ticker (else null) " +
            "so it can be enriched. " +
            "Each proof field is the VERBATIM excerpt substring backing it (null for fields you left " +
            "null). Emit one save block per source the user confirms, alongside your normal reply.",
    };

    public async IAsyncEnumerable<ChatDelta> StreamReplyAsync(
        long companyId, string accession, string doc, ExtractionNode node,
        IReadOnlyList<ChatMessage> history, string? filingType = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // The parallel worker scan runs (once per filing) before the main agent can answer — tell the
        // user so the first turn isn't a silent wait while 36 chunks fan out.
        var hasFiling = !string.IsNullOrWhiteSpace(accession) && !string.IsNullOrWhiteSpace(doc);
        if (hasFiling && !_cache.TryGetValue(FilingExtractionService.FindingsKey(accession, doc, node), out _))
            yield return new ChatDelta("status", "Scanning the filing with parallel worker agents…");

        var grounding = await GroundingAsync(companyId, accession, doc, node, filingType, ct);

        var messages = new List<DeepSeekMessage> { new("system", SystemFor(node) + grounding) };
        foreach (var m in history)
            messages.Add(new DeepSeekMessage(m.Role == "assistant" ? "assistant" : "user", m.Content));

        // No maxTokens → the lead-analyst reply runs to the model's own ceiling instead of being cut
        // off mid-answer at a fixed cap.
        await foreach (var delta in _llm.StreamAsync(messages, ct: ct))
            yield return delta;
    }

    // The main agent's grounding: the workers' findings digest (auto-scan, or a curated heading scan
    // the user kicked off). Falls back to raw section excerpts only if the scan returned nothing.
    private async Task<string> GroundingAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(accession) || string.IsNullOrWhiteSpace(doc)) return "";

        var digest = await _scan.GetOrScanDigestAsync(companyId, accession, doc, node, filingType, ct);
        if (!string.IsNullOrEmpty(digest)) return $"\n\n{digest}";

        var raw = await RawFallbackAsync(companyId, accession, doc, node, filingType, ct);
        return string.IsNullOrEmpty(raw) ? "" : $"\n\n{raw}";
    }

    // Used only when the scan yields nothing: the cleaned node-target Item excerpts, budgeted per
    // section (Items 7/8 for revenue & cost, Items 1A/7A for risk). Prefers sec2md markdown, falling
    // back to raw SEC HTML when the sidecar is down (FilingSections reads both).
    private async Task<string> RawFallbackAsync(
        long companyId, string accession, string doc, ExtractionNode node, string? filingType, CancellationToken ct)
    {
        var company = _companies.GetById(companyId);
        if (company is null || string.IsNullOrWhiteSpace(company.Cik)) return "";
        var cik = Cik.Trim(company.Cik);
        var acc = accession.Replace("-", "");
        var raw = await _sec2md.ToMarkdownAsync(cik, acc, doc, filingType, ct)
                  ?? await _client.GetFilingDocument(cik, acc, doc);
        if (raw is null) return "";
        var items = FilingSections.ItemsFor(node);
        var context = BuildContext(raw, items);
        return string.IsNullOrEmpty(context)
            ? ""
            : $"FILING EXCERPTS (Items {string.Join(", ", items)}):\n{context}";
    }

    private static string BuildContext(string raw, string[] items)
    {
        var chunks = FilingSections.Build(raw, items);
        if (chunks.Count == 0) return "";

        // Split the budget evenly across the sections present, so one long section can't crowd out
        // the others. Chunks already arrive in the node's item-priority order.
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
