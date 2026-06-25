using System.Text.Json;
using Microsoft.Extensions.Configuration;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Tests;

/// <summary>
/// Integration tests for the Perplexity call layer: the real <see cref="CounterpartyDiscoveryService"/>
/// against a scripted HTTP handler. The handler tells the planner turn (web context "low") apart from
/// the grounded-search turn ("high") and answers each with a canned Perplexity envelope, so the tests
/// drive the actual two-phase flow and the fragile hand-rolled parsing: the choicesâ†’content +
/// top-level <c>citations</c> envelope, [n]-marker resolution, truncation salvage, and cross-query dedupe.
/// </summary>
public class CounterpartyDiscoveryTests
{
    private const long OwnerId = 1;
    private const long MicrosoftId = 42;   // the counterparty an answer maps to via MatchByName

    private static CounterpartyDiscoveryService Build(ScriptedHttpHandler handler)
    {
        var owner = new Company("Apple", countryId: 1, Sector.INFORMATION_TECHNOLOGY) { Id = OwnerId };
        var microsoft = new Company("Microsoft", countryId: 1, Sector.INFORMATION_TECHNOLOGY) { Id = MicrosoftId };
        return new CounterpartyDiscoveryService(
            new HttpClient(handler) { BaseAddress = new Uri("https://api.perplexity.ai") },
            new FakeCompanyRepository(owner, microsoft),
            new FakeApiKeyProvider(new UserApiKeys(null, null, "pplx-key")));
    }

    private static async Task<List<DiscoveryEvent>> Run(
        ScriptedHttpHandler handler, string side = "CUSTOMER", bool valued = false, params string[] segments)
    {
        var events = new List<DiscoveryEvent>();
        await foreach (var e in Build(handler).DiscoverAsync(OwnerId, side, segments, valued))
            events.Add(e);
        return events;
    }

    // A Perplexity /chat/completions envelope: the model's text sits in choices[0].message.content,
    // the resolved URLs in a sibling top-level "citations" array. Both fields are built by hand here
    // the same way sonar-pro returns them.
    private static string Envelope(string content, params string[] citations)
    {
        var cites = string.Join(",", citations.Select(c => JsonSerializer.Serialize(c)));
        return "{\"choices\":[{\"message\":{\"content\":" + JsonSerializer.Serialize(content)
            + "}}],\"citations\":[" + cites + "]}";
    }

    // Branch on the request's web_search_options: the planner uses "low" context, every grounded
    // search uses "high". Lets one handler script both phases of a discovery run.
    private static ScriptedHttpHandler PlanThenSearch(string plannerEnvelope, string searchEnvelope) =>
        new((_, body) =>
        {
            using var doc = JsonDocument.Parse(body);
            var ctx = doc.RootElement.GetProperty("web_search_options")
                .GetProperty("search_context_size").GetString();
            return ScriptedHttpHandler.JsonResponse(ctx == "low" ? plannerEnvelope : searchEnvelope);
        });

    [Fact]
    public async Task DiscoverAsync_PlansThenSearches_ParsesItem_ResolvesCitation_AndMatchesExisting()
    {
        var planner = Envelope("""{"queries":["Apple's biggest customers 2024"]}""");
        var search = Envelope(
            """{"counterparties":[{"segment":"iPhone","name":"Microsoft","classification":"CUSTOMER","note":"Buys components [1]","ticker":"MSFT","country_code":"US","sector":"INFORMATION_TECHNOLOGY","source_url":"[1]"}]}""",
            "https://example.com/msft");
        var handler = PlanThenSearch(planner, search);

        var events = await Run(handler, "CUSTOMER", false, "iPhone");

        var plan = Assert.Single(events, e => e.Type == "plan");
        Assert.Single(plan.Queries!);
        Assert.Single(events, e => e.Type == "searching");

        var result = Assert.Single(events, e => e.Type == "result");
        var item = Assert.Single(result.Items!);
        Assert.Equal("Microsoft", item.Name);
        Assert.Equal("CUSTOMER", item.Side);
        Assert.Equal("MSFT", item.Ticker);
        Assert.Equal("https://example.com/msft", item.SourceUrl);   // "[1]" -> citations[0]
        Assert.Equal(MicrosoftId, item.ExistingCompanyId);          // matched against an existing company
        Assert.Contains("https://example.com/msft", result.Sources!);
    }

    [Fact]
    public async Task DiscoverAsync_SendsLowContextPlanner_ThenHighContextSearch()
    {
        var handler = PlanThenSearch(
            Envelope("""{"queries":["q1"]}"""),
            Envelope("""{"counterparties":[]}"""));

        await Run(handler, "SUPPLIER", false, "Chips");

        // First call is the planner (low + tight cap); the second is the grounded search (high + big cap).
        using var planner = JsonDocument.Parse(handler.Captured[0].Body);
        Assert.Equal("low", planner.RootElement.GetProperty("web_search_options")
            .GetProperty("search_context_size").GetString());
        Assert.Equal(700, planner.RootElement.GetProperty("max_tokens").GetInt32());
        Assert.Equal("Bearer pplx-key", handler.Captured[0].Authorization);

        using var srch = JsonDocument.Parse(handler.Captured[1].Body);
        Assert.Equal("high", srch.RootElement.GetProperty("web_search_options")
            .GetProperty("search_context_size").GetString());
        Assert.Equal(4000, srch.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task DiscoverAsync_ResolvesCitationFromNoteMarker_WhenSourceUrlNull()
    {
        var planner = Envelope("""{"queries":["q"]}""");
        // sonar-reasoning-pro shape: source_url null, the [n] marker lives in the note prose instead.
        var search = Envelope(
            """{"counterparties":[{"segment":"Cloud","name":"Acme","classification":"CUSTOMER","note":"Long-term buyer [2]","source_url":null}]}""",
            "https://example.com/one", "https://example.com/two");
        var events = await Run(PlanThenSearch(planner, search), "CUSTOMER");

        var item = Assert.Single(Assert.Single(events, e => e.Type == "result").Items!);
        Assert.Equal("https://example.com/two", item.SourceUrl);   // [2] -> citations[1]
        Assert.Null(item.ExistingCompanyId);                       // "Acme" matches nothing
    }

    [Fact]
    public async Task DiscoverAsync_SalvagesTruncatedSearchAnswer()
    {
        var planner = Envelope("""{"queries":["q"]}""");
        // finish_reason=length: the array was cut mid-stream â€” the first object completed but the outer
        // structure never closed. Parse appends "]}" to recover the complete object.
        var search = Envelope(
            """{"counterparties":[{"segment":"iPhone","name":"Microsoft","classification":"CUSTOMER","note":"x","source_url":null}""");
        var events = await Run(PlanThenSearch(planner, search), "CUSTOMER");

        var item = Assert.Single(Assert.Single(events, e => e.Type == "result").Items!);
        Assert.Equal("Microsoft", item.Name);
    }

    [Fact]
    public async Task DiscoverAsync_ValuedMode_ParsesContractValue()
    {
        var planner = Envelope("""{"queries":["q"]}""");
        var search = Envelope(
            """{"counterparties":[{"segment":"iPhone","name":"Microsoft","classification":"CUSTOMER","contract_value":2500000000,"source_url":null}]}""");
        var events = await Run(PlanThenSearch(planner, search), "CUSTOMER", valued: true);

        var item = Assert.Single(Assert.Single(events, e => e.Type == "result").Items!);
        Assert.Equal(2_500_000_000d, item.ContractValue);
    }

    [Fact]
    public async Task DiscoverAsync_DedupesSameCounterpartyAcrossQueries()
    {
        // Two planned queries, each surfacing the same company: the shared seen-set means the name is
        // emitted by whichever search lands first and suppressed in the other â€” one item total.
        var planner = Envelope("""{"queries":["q1","q2"]}""");
        var search = Envelope(
            """{"counterparties":[{"segment":"iPhone","name":"Microsoft","classification":"CUSTOMER","source_url":null}]}""");
        var events = await Run(PlanThenSearch(planner, search), "CUSTOMER");

        Assert.Equal(2, events.Count(e => e.Type == "searching"));
        Assert.Equal(1, events.Where(e => e.Type == "result").Sum(e => e.Items!.Count));
    }

    [Fact]
    public async Task DiscoverAsync_PlannerReturnsNoQueries_YieldsNothing_AndNeverSearches()
    {
        var handler = PlanThenSearch(
            Envelope("""{"queries":[]}"""),
            Envelope("""{"counterparties":[]}"""));

        var events = await Run(handler, "CUSTOMER");

        Assert.Empty(events);
        Assert.Single(handler.Captured);   // only the planner call was made
    }

    [Fact]
    public async Task DiscoverAsync_MissingKey_Throws()
    {
        var svc = new CounterpartyDiscoveryService(
            new HttpClient(ScriptedHttpHandler.Json(Envelope("""{"queries":[]}"""))) { BaseAddress = new Uri("https://api.perplexity.ai") },
            new FakeCompanyRepository(new Company("Apple", 1, Sector.INFORMATION_TECHNOLOGY) { Id = OwnerId }),
            new FakeApiKeyProvider(UserApiKeys.Empty));

        await Assert.ThrowsAsync<MissingApiKeyException>(async () =>
        {
            await foreach (var _ in svc.DiscoverAsync(OwnerId, "CUSTOMER", [])) { }
        });
    }
}
