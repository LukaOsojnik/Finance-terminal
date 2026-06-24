using System.Collections.Concurrent;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// In-memory overlay of the LIVE progress text for index-import jobs that are running in THIS process.
/// The durable job record (status, request, result, provenance) lives in the DB (<c>IndexImportJob</c> +
/// <c>IIndexImportJobRepository</c>); only the rapidly-ticking "Matching 250/500…" phase line is kept
/// here, keyed by job id, so the status endpoint can show progress without writing the DB on every tick.
///
/// Singleton, like <see cref="ScanJobStore"/>. A job continued on a different server instance simply
/// has no live line here until that instance is the one running it — the DB status still drives the UI.
/// </summary>
public class IndexImportJobStore
{
    private readonly ConcurrentDictionary<long, string> _progress = new();

    public void SetProgress(long jobId, string text) => _progress[jobId] = text;

    public string? GetProgress(long jobId) => _progress.TryGetValue(jobId, out var p) ? p : null;

    public void Clear(long jobId) => _progress.TryRemove(jobId, out _);
}
