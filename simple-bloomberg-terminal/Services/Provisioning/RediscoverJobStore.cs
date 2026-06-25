using System.Collections.Concurrent;
using simple_bloomberg_terminal.Models.ViewModels;

namespace simple_bloomberg_terminal.Services.Provisioning;

/// <summary>
/// One detached private-company profile re-discovery: its live status and phase text. Lives in
/// <see cref="RediscoverJobStore"/> (a singleton) so the ~90s Perplexity call outlives the HTTP
/// request that began it — the bottom-right notification widget (which polls /extraction/scan-jobs,
/// where these are merged in) learns when it finishes. Reuses <see cref="ScanJobStatus"/>.
/// </summary>
public class RediscoverJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");
    public long CompanyId { get; init; }
    public string CompanyName { get; init; } = "";

    public ScanJobStatus Status { get; set; } = ScanJobStatus.Running;
    public string Progress { get; set; } = "Queued…"; // live phase text shown while running
    public string? Result { get; set; }              // proposed-vs-current summary shown for review
    public string? Error { get; set; }

    // The mapped values awaiting the user's verdict. Nothing is written to the DB until they ACCEPT in
    // the widget (which applies this); REJECT just dismisses the job. Null until discovery finishes.
    public CompanyCreateModel? Proposed { get; set; }
    public bool Applied { get; set; }                // true once the user accepted and it was saved
    public IReadOnlyList<string> Sources { get; set; } = []; // web pages sonar cited, shown as links
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}

/// <summary>
/// Tracks detached re-discovery jobs across requests. Singleton (server-wide), like
/// <see cref="ScanJobStore"/>: the browser holds the job ids in localStorage and asks the store for
/// their status. Single-user terminal, so there is no per-user partitioning.
/// </summary>
public class RediscoverJobStore
{
    private readonly ConcurrentDictionary<string, RediscoverJob> _jobs = new();

    public void Add(RediscoverJob job) => _jobs[job.Id] = job;
    public RediscoverJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;
    public bool Remove(string id) => _jobs.TryRemove(id, out _);
}
