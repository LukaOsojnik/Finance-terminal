namespace simple_bloomberg_terminal.Models.Enums;

/// <summary>
/// Lifecycle of a persisted index-import job. <c>Partial</c> is the resumable state: the import ran
/// but its FMP auto-provisioning was cut short (the runner had no FMP key, or hit FMP's daily cap), so
/// some members still aren't linked. Any user can then <em>continue</em> the job under their own key —
/// the re-run re-links what already exists (free, via the SEC CIK map) and only spends FMP quota on the
/// members still missing. <c>Done</c> means provisioning ran to completion; <c>Error</c> means the run
/// failed before producing an index.
/// </summary>
public enum ImportJobStatus
{
    Running,
    Done,
    Partial,
    Error
}
