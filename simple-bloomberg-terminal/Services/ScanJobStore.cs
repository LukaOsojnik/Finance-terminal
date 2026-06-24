using System.Collections.Concurrent;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services;

public enum ScanJobStatus { Running, Done, Error }

/// <summary>One parallel agent call's live state within a section. <see cref="Titles"/> are the
/// sub-headings this single call bundled (packing); <see cref="Status"/> is Queued/Running/Done/Error.</summary>
public class ScanChunkState
{
    public IReadOnlyList<string> Titles { get; init; } = [];
    public string Status { get; set; } = "Queued";
    public int Found { get; set; }
    // The exact text the worker agent saw (system + user prompt) and the raw model reply, captured so
    // the widget can drill into one agent call and show what it got and how it answered. Empty until
    // the chunk finishes (Done/Error); on Error, Response holds the failure message.
    public string Prompt { get; set; } = "";
    public string Response { get; set; } = "";
}

/// <summary>An SEC Item group (e.g. "Item 7") and the agent calls scanning it — the widget shows
/// one expandable box per section, drilling into its <see cref="Chunks"/>.</summary>
public class ScanSection
{
    public string Item { get; init; } = "";
    public List<ScanChunkState> Chunks { get; } = new();
}

/// <summary>
/// One detached auto-scan: the filing context, its live status, the scan report, and the auto
/// AI summary the worker produces when it finishes. Lives in <see cref="ScanJobStore"/> (a
/// singleton) so it outlives the HTTP request that started it — the page that kicked it off can
/// navigate away and the notification widget polls the store to learn when it's done.
/// </summary>
public class ScanJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public long CompanyId { get; init; }
    public string CompanyName { get; init; } = "";
    public string Accession { get; init; } = "";
    public string Doc { get; init; } = "";
    public string Node { get; init; } = "REVENUE";
    public string? Form { get; init; }
    public string FilingLabel { get; init; } = "";   // e.g. "10-K 2024-01-31" for the widget header

    public ScanJobStatus Status { get; set; } = ScanJobStatus.Running;
    public string Progress { get; set; } = "Queued…"; // live phase text shown while running

    // The live parallel-scan tree: one section per SEC Item, each holding its agent-call states.
    // Mutated from the scan's progress callback (concurrent workers) and read by the poll DTO, so both
    // sides take `SectionsLock` — these are plain mutable objects, not thread-safe on their own.
    public List<ScanSection> Sections { get; } = new();
    public List<ScanChunkState> ChunkList { get; } = new();  // flat, index-aligned with the scan plan
    public object SectionsLock { get; } = new();
    public AutoScanResult? Report { get; set; }      // counts + picked headings
    public XbrlView? Xbrl { get; set; }              // the audited tagged facts (COST/REVENUE), shown above the Item tree
    public string Summary { get; set; } = "";        // auto AI prose shown first in the widget
    public string? Error { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }

    // A follow-up chat reply, generated detached so it survives the user navigating away. The
    // browser POSTs a message, the background task streams the model into these buffers, and the
    // widget polls them — no page-bound fetch to abort on navigation.
    public bool Replying { get; set; }
    public string ReplyBuffer { get; set; } = "";    // incremental answer text
    public string ReplyThink { get; set; } = "";     // incremental reasoning/thinking
    public string? ReplyError { get; set; }
}

/// <summary>
/// Tracks detached scan jobs across requests. Singleton (server-wide): the only shared state a
/// fire-and-forget scan needs. The browser tracks its own job ids in localStorage and asks this
/// store for their status — there is no per-user partitioning (single-user terminal).
/// </summary>
public class ScanJobStore
{
    private readonly ConcurrentDictionary<string, ScanJob> _jobs = new();

    public void Add(ScanJob job) => _jobs[job.Id] = job;

    public ScanJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}
