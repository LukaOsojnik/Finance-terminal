namespace simple_bloomberg_terminal.Services.Clients.Edgar;

/// <summary>
/// URLs into the EDGAR Archives tree. One place for the path shape
/// <c>/Archives/edgar/data/{cik}/{accession-without-dashes}/{document}</c>, which is stored on
/// <see cref="Models.Entities.Filing.PrimaryDocUrl"/> and must be identical whether the row was
/// written by the extraction page, a batch save from the chat, or a backfill.
/// </summary>
public static class EdgarArchive
{
    /// <summary>The absolute URL of one document inside a filing, or null when any part is missing
    /// (a company with no CIK, a filing whose primary document EDGAR did not name).</summary>
    public static string? DocUrl(string? cik, string? accession, string? document)
    {
        if (string.IsNullOrWhiteSpace(cik) || string.IsNullOrWhiteSpace(accession) ||
            string.IsNullOrWhiteSpace(document)) return null;
        return $"https://www.sec.gov/Archives/edgar/data/{Cik.Trim(cik)}/{accession.Replace("-", "")}/{document}";
    }
}
