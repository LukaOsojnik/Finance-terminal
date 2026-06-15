namespace simple_bloomberg_terminal.Services;

/// <summary>
/// SEC CIK string shaping. The submissions endpoint wants the 10-char zero-padded form
/// (<c>Pad</c>); Archives URLs and document fetches want the leading zeros stripped
/// (<c>Trim</c>); user/imported input may carry stray characters, so <c>Normalize</c>
/// filters to digits before padding. Centralizes the pad/trim that was copy-pasted across
/// the stock controller, stock/extraction services and the FMP/provisioning mappers.
/// </summary>
public static class Cik
{
    /// <summary>Zero-pad a digit-only CIK to the 10-char form SEC submission APIs expect.</summary>
    public static string Pad(string cik) => cik.PadLeft(10, '0');

    /// <summary>Strip leading zeros for SEC Archives URL paths / document fetches.</summary>
    public static string Trim(string cik) => cik.TrimStart('0');

    /// <summary>Pull digits out of a messy CIK then zero-pad to 10; null if blank or no digits.</summary>
    public static string? Normalize(string? cik)
    {
        if (string.IsNullOrWhiteSpace(cik)) return null;
        var digits = new string(cik.Where(char.IsDigit).ToArray());
        return digits.Length == 0 ? null : digits.PadLeft(10, '0');
    }
}
