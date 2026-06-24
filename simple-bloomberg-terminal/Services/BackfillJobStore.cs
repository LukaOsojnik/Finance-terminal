using System.Collections.Concurrent;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// One detached Backfill run's live state: the per-company progress lines as they resolve, a done/error
/// flag, the final summary, and a <see cref="CancellationTokenSource"/> so the browser's Close button can
/// actually abort the in-flight LLM calls (cancelling the token aborts the current DeepSeek request and
/// stops the loop). Lives in <see cref="BackfillJobStore"/> (singleton) so it outlives the HTTP request
/// that started it and the page can poll it.
/// </summary>
public class BackfillJob
{
    public string Id { get; init; } = Guid.NewGuid().ToString("n");

    // Progress lines, appended as each company resolves. Read by the poll endpoint (which slices from the
    // browser's last-seen offset), so both sides take the lock — List is not thread-safe on its own.
    public List<string> Lines { get; } = new();
    public object Lock { get; } = new();

    public bool Done { get; set; }
    public string? Error { get; set; }

    // The final summary (counts + lists) the results popup renders once the run finishes.
    public BackfillResult? Result { get; set; }

    public CancellationTokenSource Cts { get; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Tracks detached Backfill jobs across requests. Singleton, like <see cref="ScanJobStore"/> /
/// <see cref="IndexImportJobStore"/> — single-user terminal, so no per-user partitioning.
/// </summary>
public class BackfillJobStore
{
    private readonly ConcurrentDictionary<string, BackfillJob> _jobs = new();

    public void Add(BackfillJob job) => _jobs[job.Id] = job;

    public BackfillJob? Get(string id) => _jobs.TryGetValue(id, out var j) ? j : null;

    public void Remove(string id) => _jobs.TryRemove(id, out _);
}

/// <summary>An <see cref="IProgress{T}"/> that invokes its callback synchronously on the reporting
/// thread, so progress lines land in order. (The framework's <c>Progress&lt;T&gt;</c> posts to a sync
/// context / the thread pool, which can reorder rapid reports from a detached task.)</summary>
public sealed class SyncProgress<T>(Action<T> onReport) : IProgress<T>
{
    public void Report(T value) => onReport(value);
}
