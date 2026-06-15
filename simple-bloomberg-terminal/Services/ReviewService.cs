using System.Text.Json;
using simple_bloomberg_terminal.Models.Entities;
using simple_bloomberg_terminal.Models.Enums;
using simple_bloomberg_terminal.Repositories;

namespace simple_bloomberg_terminal.Services;

public class ReviewService : IReviewService
{
    private readonly ISourceFieldReviewRepository _reviews;
    private readonly IDeepSeekClient _llm;
    private readonly string _model;

    public ReviewService(ISourceFieldReviewRepository reviews, IDeepSeekClient llm, IConfiguration config)
    {
        _reviews = reviews;
        _llm = llm;
        _model = config["DeepSeek:ReviewerModel"] ?? "deepseek-v4-pro";
    }

    private const string System =
        "You are a financial analyst-reviewer. You are given ONE extracted cell from a company's " +
        "revenue, cost or risk data and the verbatim proof text it was drawn from (an SEC filing or XBRL fact). " +
        "Judge only whether the proof supports the claimed value for that one field. Arithmetic, unit " +
        "scaling (thousands/millions), and clear inference are allowed; an unrelated or contradicting " +
        "snapshot fails. Reply with JSON only, no prose, no code fences: " +
        "{\"mark\": 0 or 1, \"rationale\": \"one short sentence\"}.";

    public async Task<ReviewRunResult> ReviewCompanyAsync(long companyId, CancellationToken ct = default)
    {
        // Only this company's unreviewed cells that actually carry proof text.
        var pending = _reviews.GetByCompany(companyId)
            .Where(r => r.Mark == null && !string.IsNullOrWhiteSpace(r.ReferenceSnapshot))
            .ToList();

        int passed = 0, failed = 0, skipped = 0;
        foreach (var review in pending)
        {
            var claimed = ClaimedValue(review);
            if (string.IsNullOrWhiteSpace(claimed)) { skipped++; continue; }

            var prompt =
                $"Field: {review.Field}\n" +
                $"Claimed value: {claimed}\n\n" +
                $"Proof text (verbatim):\n\"\"\"\n{review.ReferenceSnapshot}\n\"\"\"\n\n" +
                "Does the proof support the claimed value for this field?";

            string raw;
            try { raw = await _llm.CompleteAsync(_model, System, prompt, maxTokens: 300, jsonObject: true, ct: ct); }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { skipped++; continue; }

            if (!TryParseVerdict(raw, out var mark, out var rationale)) { skipped++; continue; }

            review.Mark = mark;
            review.Rationale = rationale;
            review.ReviewedAt = DateTime.UtcNow;
            review.ReviewerModel = _model;
            _reviews.Update(review);

            if (mark == 1) passed++; else failed++;
        }

        return new ReviewRunResult(passed + failed, passed, failed, skipped);
    }

    // The value being judged. Prefer the value frozen at reference time (what the proof was attached
    // to); fall back to the live source-row cell for the numeric/text fields.
    private static string? ClaimedValue(SourceFieldReview review)
    {
        if (!string.IsNullOrWhiteSpace(review.ReferencedValue)) return review.ReferencedValue;

        // Read the live cell from whichever source row this review belongs to. RELATED_COMPANY's
        // name isn't loaded here, so it falls through to null (and is judged off ReferencedValue).
        if (review.RevenueSource is { } rev)
            return review.Field switch
            {
                ReviewableField.VALUE          => rev.Value?.ToString(),
                ReviewableField.PERCENTAGE     => rev.Percentage?.ToString(),
                ReviewableField.NAME           => rev.Name,
                ReviewableField.CLASSIFICATION => rev.SourceType.ToString(),
                _                              => null
            };
        if (review.CostSource is { } cost)
            return review.Field switch
            {
                ReviewableField.VALUE          => cost.Value?.ToString(),
                ReviewableField.PERCENTAGE     => cost.Percentage?.ToString(),
                ReviewableField.NAME           => cost.Name,
                ReviewableField.CLASSIFICATION => cost.CostBase.ToString(),
                _                              => null
            };
        if (review.CompanyRisk is { } risk)
            return review.Field switch
            {
                ReviewableField.NAME           => risk.Name,
                ReviewableField.CLASSIFICATION => risk.Scope.ToString(),
                ReviewableField.NOTE           => risk.Note,
                _                              => null
            };
        return null;
    }

    // The model is asked for bare JSON, but tolerate ```json fences / stray prose just in case.
    private static bool TryParseVerdict(string raw, out int mark, out string rationale)
    {
        mark = 0; rationale = "";
        using var doc = LlmJson.ParseObject(raw);
        if (doc is null) return false;
        var root = doc.RootElement;
        mark = root.GetProperty("mark").GetInt32() == 1 ? 1 : 0;
        rationale = root.TryGetProperty("rationale", out var r) ? (r.GetString() ?? "") : "";
        return true;
    }
}
