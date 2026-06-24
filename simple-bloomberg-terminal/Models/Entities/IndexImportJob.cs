using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// A persisted, globally-visible index import. Unlike the old in-memory job (which died with the
/// process and was visible only to the browser that started it), this row survives restarts and is
/// shared across users: when one user's FMP key runs out mid-import the job lands in
/// <see cref="ImportJobStatus.Partial"/>, and another user can <em>continue</em> it under their own key.
///
/// The original import request is stored verbatim (<see cref="Code"/>…<see cref="Region"/>) so a
/// continue can re-run the exact same import without the caller re-supplying it. The re-run is
/// idempotent — it upserts the index by code and only auto-provisions members not yet in the DB — so
/// continuing simply folds in the members the previous key couldn't add.
///
/// Live phase text is NOT stored here (it ticks too often to write each update); it lives in the
/// in-memory <c>IndexImportJobStore</c> overlay while a run is active.
/// </summary>
public class IndexImportJob
{
    [Key]
    public long Id { get; set; }

    public string Label { get; set; } = "";   // what's being imported, for the jobs list

    // ── Stored import request, replayed verbatim on continue ──
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public string? WikiPage { get; set; }
    public string? EtfTicker { get; set; }
    public Sector? Sector { get; set; }
    public string? Region { get; set; }

    public ImportJobStatus Status { get; set; } = ImportJobStatus.Running;
    public long? IndexId { get; set; }         // set once the import has produced/updated an index

    // Last run's coverage, shown in the jobs list so the gap a continue would close is visible.
    public int TotalConstituents { get; set; }
    public int Matched { get; set; }
    public int Provisioned { get; set; }

    public string? Message { get; set; }       // success/coverage summary
    public string? Error { get; set; }

    // Provenance for the multi-user flow: who began it and who last continued it (display names, not
    // FKs — the jobs list only needs to show "started by X, continued by Y").
    public string? StartedBy { get; set; }
    public string? ContinuedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}
