using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using simple_bloomberg_terminal.Models.ViewModels;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

// Perplexity request shape: OpenAI-compatible plus web_search_options (Perplexity-only). Kept separate
// from DeepSeekRequest so a DeepSeek call never carries a Perplexity field. Reuses DeepSeekMessage.
public record PerplexityRequest(
    string Model,
    List<DeepSeekMessage> Messages,
    [property: JsonPropertyName("max_tokens"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTokens,
    [property: JsonPropertyName("web_search_options")] PerplexityWebSearchOptions? WebSearchOptions = null);

// search_context_size (low|medium|high) controls how much web content sonar pulls in before answering.
public record PerplexityWebSearchOptions(
    [property: JsonPropertyName("search_context_size")] string SearchContextSize);

/// <summary>
/// Typed HttpClient for Perplexity's OpenAI-compatible /chat/completions, used only to discover the
/// named suppliers/customers behind a company's revenue/cost segments. Reuses the DeepSeek
/// request/response records (same wire shape); the only difference is the base URL, key and model
/// from the "Perplexity" config section. sonar searches the web itself, so no separate search API is
/// wired — it returns an already-grounded answer. Light shaping (prompt, parse, match-existing) lives
/// here; no extra layer.
/// </summary>
public class CounterpartyDiscoveryService : ICounterpartyDiscovery
{
    private readonly HttpClient _http;
    private readonly ICompanyRepository _companies;
    private readonly IUserApiKeyProvider _keys;
    private readonly string _model;

    public CounterpartyDiscoveryService(HttpClient http, ICompanyRepository companies,
        IConfiguration config, IUserApiKeyProvider keys)
    {
        _http = http;
        _companies = companies;
        _keys = keys;
        _model = config.GetSection("Perplexity")["Model"] ?? "sonar-pro";
    }

    // The user's Perplexity key, or throw the "add your key" signal the front-end turns into a popup.
    private Task<string> KeyAsync(CancellationToken ct) =>
        _keys.RequireAsync(k => k.Perplexity, MissingApiKeyException.Perplexity, ct);

    private const int RecencyYears = 5;   // discovery covers relationships active in the last N years
    private const int MaxQueries = 6;     // cap planner output so cost (one paid search per query) stays bounded
    private const int MaxConcurrent = 3;  // cap parallel Perplexity calls so a burst doesn't trip its rate limit

    // Tail of the search prompt: the per-company fields sonar must return and the exact JSON shape.
    // Built (not a const) so the classification rule is injected directly — no string.Format/{0}, whose
    // placeholder would clash with the literal { } braces in the embedded JSON template — and so the
    // valued mode can add a contract_value field + key without a second copy of the whole spec.
    private static string FieldsSpec(string classRule, bool valued)
    {
        // Only valued mode asks for (and emits) a dollar figure; plain mode keeps the original shape.
        var valueField = valued
            ? "contract_value (estimated USD value of the relationship/contract as a PLAIN NUMBER in " +
              "dollars — no currency symbol, units or commas, e.g. 2500000000 for $2.5 billion; null if " +
              "unknown), "
            : "";
        var valueKey = valued ? ",\"contract_value\":null" : "";
        return
            "For each provide: segment (the business segment/area it relates to), name (the company), " +
            $"classification ({classRule}), note (one short sentence on the relationship), " + valueField +
            "ticker (the company's stock ticker symbol if publicly listed, e.g. MSFT, NVDA, 2317.TW; null " +
            "if private or unknown), country_code (ISO-2 code, e.g. US, DE; null if unknown), sector (one " +
            "of ENERGY, MATERIALS, INDUSTRIALS, CONSUMER_DISCRETIONARY, CONSUMER_STAPLES, HEALTH_CARE, " +
            "FINANCIALS, INFORMATION_TECHNOLOGY, COMMUNICATION_SERVICES, UTILITIES, REAL_ESTATE; null if " +
            "unknown), source_url (a URL backing it; null if none). Reply with JSON only, no prose, no " +
            "code fences: {\"counterparties\":[{\"segment\":\"\",\"name\":\"\",\"classification\":\"\"," +
            "\"note\":null" + valueKey + ",\"ticker\":null,\"country_code\":null,\"sector\":null," +
            "\"source_url\":null}]}. If you find none, reply {\"counterparties\":[]}.";
    }

    // Stream discovery Perplexity-style: plan a handful of focused sub-queries, then run each as its own
    // grounded web search and emit results as they land. Dedupe across queries by company name so the
    // same counterparty isn't suggested twice. Persists nothing.
    public async IAsyncEnumerable<DiscoveryEvent> DiscoverAsync(
        long companyId, string side, IReadOnlyList<string> segments, bool valued = false,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var company = _companies.GetById(companyId);
        if (company is null) yield break;

        var supplier = string.Equals(side, "SUPPLIER", StringComparison.OrdinalIgnoreCase);
        var where = company.Country?.Name is { Length: > 0 } c ? $" ({c})" : "";
        var thisYear = DateTime.UtcNow.Year;
        var firstYear = thisYear - (RecencyYears - 1);

        // Phase 1 — planner decomposes the company + its segments into several focused search queries.
        var queries = await PlanQueriesAsync(company.Name, where, supplier, valued, segments, firstYear, thisYear, ct);
        if (queries.Count == 0) yield break;
        yield return new DiscoveryEvent("plan", Queries: queries);

        // Phase 2 — run every sub-query in PARALLEL, each its own grounded search. Events drain through a
        // channel so results surface as soon as each query lands (order is whoever finishes first), and a
        // failed query yields an empty result instead of killing the run (catch-all per task). Dedupe by
        // company name across queries with a concurrent set, since tasks add concurrently.
        var seen = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        var channel = Channel.CreateUnbounded<DiscoveryEvent>();

        // Cap how many searches hit Perplexity at once: a burst of 6 parallel calls can trip its rate
        // limit (429), which would fail every query instantly. This keeps the fan-out but bounded.
        using var gate = new SemaphoreSlim(MaxConcurrent);
        var tasks = queries.Select(async q =>
        {
            await channel.Writer.WriteAsync(new DiscoveryEvent("searching", Query: q), ct);
            // Catch-ALL: any failure (transport, timeout, parse, cancellation) still REPORTS back — empty
            // items plus the reason — so the row resolves (no eternal spin) AND shows why it found nothing
            // instead of a misleading "0 found". One bad query must never strand the others.
            IReadOnlyList<CounterpartySuggestion> items = [];
            IReadOnlyList<string> sources = [];
            string? error = null;
            await gate.WaitAsync(ct);
            try { (items, sources) = await SearchAsync(q, supplier, valued, ct); }
            catch (Exception ex) { error = ex is HttpRequestException h ? $"search failed ({(int?)h.StatusCode}{h.StatusCode})" : ex.GetType().Name; }
            finally { gate.Release(); }
            var fresh = items.Where(i => seen.TryAdd(i.Name, 0)).ToList();
            await channel.Writer.WriteAsync(new DiscoveryEvent("result", Query: q, Items: fresh, Sources: sources, Error: error), ct);
        }).ToList();

        // Close the channel once every search has reported, so the reader loop below terminates.
        _ = Task.Run(async () =>
        {
            try { await Task.WhenAll(tasks); } catch { /* per-task errors already became empty results */ }
            finally { channel.Writer.Complete(); }
        }, ct);

        await foreach (var e in channel.Reader.ReadAllAsync(ct))
            yield return e;
    }

    // Phase 1 call: ask sonar for a short list of focused, distinct web-search queries. Each query should
    // target a different segment / product line / supplier tier so together they pull diverse sources —
    // that diversity is the whole point of decomposing instead of one broad call. Light web context is
    // enough here (it only needs to know the company's real segments), so this is the cheap call.
    private async Task<List<string>> PlanQueriesAsync(
        string name, string where, bool supplier, bool valued, IReadOnlyList<string> segments,
        int firstYear, int thisYear, CancellationToken ct)
    {
        var what = supplier ? "suppliers (companies it buys from)" : "customers (companies that buy from it)";
        var kind = supplier ? "cost" : "revenue";
        // Valued mode targets the LARGEST counterparties and the size of each deal; the contract-value
        // emphasis steers the planner toward questions that surface dollar figures (deal/contract sizes).
        var goal = valued
            ? $"uncover the BIGGEST NAMED {what} of a single public company AND the dollar value of each " +
              "relationship (contract size, annual spend, or revenue/cost contribution)"
            : $"uncover the NAMED {what} of a single public company";
        var angle = valued
            ? $"a specific {kind} segment, product line, region, or the size/value of the deal"
            : $"a specific {kind} segment, product line, region, or supplier/customer tier";
        var system =
            $"You plan web research to {goal}. Produce a " +
            $"list of focused, DISTINCT research questions that, each run separately, will surface those " +
            $"companies from different angles — {angle} — so together they cover diverse, primary sources " +
            $"(filings, IR pages, industry press). Scope every question to {firstYear}-{thisYear}. " +
            // The downstream searcher is an LLM, not a search box: plain questions work, search-engine
            // operators (the over-constrained Google-dork style) make it find nothing.
            "Write each as a PLAIN-LANGUAGE question a researcher would ask — NEVER use search operators " +
            "(no quotation marks, no site:, no OR/AND, no minus-exclusions, no date ranges like 2022..2026). " +
            $"Return between 2 and {MaxQueries} questions, most specific first. " +
            "Reply with JSON only, no prose, no code fences: {\"queries\":[\"\",\"\"]}.";
        var target = valued ? $"the biggest {what} and the value of each deal" : $"the named {what}";
        var user = segments.Count > 0
            ? $"Company: {name}{where}. {(supplier ? "Cost" : "Revenue")} segments: {string.Join("; ", segments)}. " +
              $"Plan queries to find {target}, covering each segment."
            : $"Company: {name}{where}. Identify its main {kind} segments yourself, then plan queries to find " +
              $"{target} across those segments.";

        // Planner output is small; a tight cap is plenty and keeps it cheap. "low" web context = enough to
        // ground real segment names without paying for a deep read.
        var (answer, _) = await CallAsync(system, user, "low", maxTokens: 700, ct);
        return ParseQueries(answer);
    }

    // Phase 2 call: one grounded search for a single planned query. Returns the named counterparties it
    // surfaced (parsed + matched against existing companies) AND the web pages it fetched (citation URLs,
    // for the live "fetched" list). "high" web context for primary sources.
    private async Task<(IReadOnlyList<CounterpartySuggestion> items, IReadOnlyList<string> sources)> SearchAsync(
        string query, bool supplier, bool valued, CancellationToken ct)
    {
        var who = supplier ? "SUPPLIERS (companies it BUYS from)" : "CUSTOMERS (companies that BUY from it)";
        var classRule = supplier ? "exactly one of COGS, OPEX" : "always 'CUSTOMER'";
        // Valued mode: bias toward the largest relationships and require a dollar estimate per row.
        var valuedRule = valued
            ? "Focus on the BIGGEST such relationships and, for each, estimate the USD contract value from " +
              "the sources (deal size, annual spend, or revenue/cost contribution). "
            : "";
        var system =
            $"You research a single public company's {(supplier ? "suppliers" : "customers")} from current " +
            $"web sources, answering ONE focused research query. Return every real, NAMED {who} the query " +
            "surfaces — never generic labels like 'consumers' or 'various suppliers'. " + valuedRule +
            "Prefer recent, primary sources. " + FieldsSpec(classRule, valued);

        // Company-wide queries can list many companies; the cap must clear the whole JSON or it gets cut
        // mid-array (finish_reason=length) — Parse's salvage recovers a partial cut.
        var (answer, citations) = await CallAsync(system, query, "high", maxTokens: 4000, ct);
        return (Parse(answer, supplier, citations), citations);
    }

    // One Perplexity /chat/completions turn. Parses the envelope by hand (not the typed DeepSeekResponse):
    // sonar-pro cites sources with numbered markers like "[10]" in the content and puts the real URLs in
    // a top-level "citations" array, so callers get both the answer text and that array to resolve them.
    private async Task<(string answer, List<string> citations)> CallAsync(
        string system, string user, string contextSize, int maxTokens, CancellationToken ct)
    {
        var req = new PerplexityRequest(
            Model: _model,
            Messages: [new DeepSeekMessage("system", system), new DeepSeekMessage("user", user)],
            MaxTokens: maxTokens,
            WebSearchOptions: new PerplexityWebSearchOptions(contextSize));

        using var httpReq = new HttpRequestMessage(HttpMethod.Post, "/chat/completions")
        {
            Content = JsonContent.Create(req),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", await KeyAsync(ct)) }
        };
        var resp = await _http.SendAsync(httpReq, ct);
        resp.EnsureSuccessStatusCode();

        using var env = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct));
        var root = env.RootElement;
        var answer = root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var msg) && msg.TryGetProperty("content", out var content)
            ? content.GetString() ?? "" : "";
        var citations = new List<string>();
        if (root.TryGetProperty("citations", out var cits) && cits.ValueKind == JsonValueKind.Array)
            foreach (var u in cits.EnumerateArray())
                if (u.ValueKind == JsonValueKind.String && u.GetString() is { Length: > 0 } url) citations.Add(url);
        return (answer, citations);
    }

    // Pull the planner's {"queries":[...]} out of its answer, tolerant of fences/prose around it.
    // Trims, drops blanks, caps the count.
    private static List<string> ParseQueries(string answer)
    {
        var list = new List<string>();
        using var doc = LlmJson.ParseObject(answer);
        if (doc is null || !doc.RootElement.TryGetProperty("queries", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var q in arr.EnumerateArray())
            if (q.ValueKind == JsonValueKind.String && q.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
            {
                list.Add(s.Trim());
                if (list.Count >= MaxQueries) break;
            }
        return list;
    }

    // Pull counterparties out of the model's JSON, tolerant of code fences/prose around it (sonar
    // doesn't guarantee a bare object). Side is fixed by the call; each name is matched against
    // existing companies so the page can offer "link" instead of "create + link".
    private IReadOnlyList<CounterpartySuggestion> Parse(string answer, bool supplier, IReadOnlyList<string> citations)
    {
        // Salvage a truncated response (finish_reason=length): the array was cut mid-stream so the
        // outer structure never closed — closing it with `]}` recovers every complete object.
        using var doc = LlmJson.ParseObject(answer, "]}");
        if (doc is null) return [];

        var side = supplier ? "SUPPLIER" : "CUSTOMER";
        var list = new List<CounterpartySuggestion>();
        if (!doc.RootElement.TryGetProperty("counterparties", out var arr) ||
            arr.ValueKind != JsonValueKind.Array) return list;

        foreach (var el in arr.EnumerateArray())
        {
            var name = LlmJson.Str(el, "name");
            if (string.IsNullOrWhiteSpace(name)) continue;

            var classification = LlmJson.Str(el, "classification") ?? (supplier ? "COGS" : "CUSTOMER");

            // Fuzzy match so "Microsoft Corporation" maps to an existing "Microsoft".
            var existing = _companies.MatchByName(name);

            list.Add(new CounterpartySuggestion(
                Name: name!,
                Side: side,
                Segment: LlmJson.Str(el, "segment") ?? "",
                Classification: classification,
                Note: LlmJson.Str(el, "note"),
                SourceUrl: ResolveSource(el, citations),
                CountryCode: LlmJson.Str(el, "country_code"),
                Sector: LlmJson.Str(el, "sector"),
                Ticker: LlmJson.Str(el, "ticker"),
                ExistingCompanyId: existing?.Id,
                // Present only in valued mode; absent/null in plain mode.
                ContractValue: LlmJson.Num(el, "contract_value")));
        }
        return list;
    }

    // Resolve a counterparty's citation to a real URL. Models cite differently: base sonar puts an
    // "https://…" string in source_url; sonar-pro puts a [n] marker there; sonar-reasoning-pro leaves
    // source_url null and embeds the [n] marker in the note prose. Try the field first, then fall back
    // to the first [n] marker in the note — both resolve against the response's citations list.
    private static string? ResolveSource(JsonElement el, IReadOnlyList<string> citations)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;

        if (el.TryGetProperty("source_url", out var v) && FromField(v, citations) is { } fromField)
            return fromField;

        // Fallback: the first [n] citation marker the model dropped inside the note text.
        var note = LlmJson.Str(el, "note");
        if (note != null && Regex.Match(note, @"\[(\d+)\]") is { Success: true } m
            && int.TryParse(m.Groups[1].Value, out var marker))
            return Cite(marker, citations);

        return null;
    }

    // source_url shapes: a real "https://…" string; a 1-based citation marker as a number (10),
    // a string ("[10]") or a single-element array ([10]); or null/"null".
    private static string? FromField(JsonElement v, IReadOnlyList<string> citations)
    {
        switch (v.ValueKind)
        {
            case JsonValueKind.String:
                var s = v.GetString();
                if (string.IsNullOrWhiteSpace(s) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase)) return null;
                if (s.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return s;
                return Cite(int.TryParse(s.Trim('[', ']', ' '), out var si) ? si : -1, citations);
            case JsonValueKind.Number:
                return Cite(v.TryGetInt32(out var n) ? n : -1, citations);
            case JsonValueKind.Array:
                foreach (var e in v.EnumerateArray())
                    if (e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var m)) return Cite(m, citations);
                return null;
            default:
                return null;
        }
    }

    private static string? Cite(int oneBased, IReadOnlyList<string> citations) =>
        oneBased >= 1 && oneBased <= citations.Count ? citations[oneBased - 1] : null;
}
