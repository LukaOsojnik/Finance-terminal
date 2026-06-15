using System.Globalization;
using System.Text.Json;

namespace simple_bloomberg_terminal.Services;

/// <summary>
/// Shared parsing helpers for LLM JSON replies. Models are asked for a bare JSON object but in
/// practice wrap it in code fences / prose and emit numbers as strings (sometimes with $, commas
/// or %), so every service that reads a model reply needs the same tolerant slice-and-parse. This
/// holds that logic once.
/// </summary>
public static class LlmJson
{
    /// <summary>
    /// Slice the first <c>{</c>…last <c>}</c> object out of a model answer (tolerant of code fences
    /// and surrounding prose) and parse it. Returns null if there is no parseable object. The caller
    /// owns the returned document — dispose it with <c>using</c>.
    /// </summary>
    /// <param name="salvageSuffix">
    /// Optional text appended and retried if the first parse fails — used to recover a response cut
    /// off mid-array (finish_reason=length) by closing the structure ourselves (e.g. <c>"]}"</c>).
    /// </param>
    public static JsonDocument? ParseObject(string answer, string? salvageSuffix = null)
    {
        var start = answer.IndexOf('{');
        var end = answer.LastIndexOf('}');
        if (start < 0 || end <= start) return null;

        var json = answer[start..(end + 1)];
        try { return JsonDocument.Parse(json); }
        catch (JsonException)
        {
            if (salvageSuffix is null) return null;
            try { return JsonDocument.Parse(json + salvageSuffix); }
            catch (JsonException) { return null; }
        }
    }

    /// <summary>
    /// Read a string field. Returns null for a missing/non-string value, and treats the literal
    /// string <c>"null"</c> (which models sometimes emit instead of JSON null) as absent.
    /// </summary>
    public static string? Str(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String)
            return null;
        var s = v.GetString();
        return string.IsNullOrWhiteSpace(s) || string.Equals(s, "null", StringComparison.OrdinalIgnoreCase) ? null : s;
    }

    /// <summary>
    /// Read a numeric field. Accepts a JSON number or a numeric string; despite the prompt models
    /// sometimes slip in $, commas or %, so strip those before parsing (invariant culture). Anything
    /// non-numeric => null.
    /// </summary>
    public static double? Num(JsonElement el, string prop)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty(prop, out var v)) return null;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = s.Replace("$", "").Replace(",", "").Replace("%", "").Trim();
        return double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }
}
