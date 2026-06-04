namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Phase-2 AI reviewer (Mode A: human wrote the value + picked the proof, AI judges it). Pulls a
/// company's unreviewed <c>SourceFieldReview</c> rows and, for each, asks the model whether the frozen
/// proof snapshot supports the claimed cell value — writing <c>Mark</c> (1 pass / 0 fail) + a
/// one-line <c>Rationale</c> back in place.
/// </summary>
public interface IReviewService
{
    Task<ReviewRunResult> ReviewCompanyAsync(long companyId, CancellationToken ct = default);
}

/// <summary>Tally of one review pass, surfaced to the page.</summary>
public record ReviewRunResult(int Reviewed, int Passed, int Failed, int Skipped);
