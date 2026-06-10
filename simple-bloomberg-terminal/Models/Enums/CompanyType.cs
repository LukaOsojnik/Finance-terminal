namespace simple_bloomberg_terminal.Models.Enums;

// Whether a company is publicly listed (has a ticker -> real FMP/Yahoo data) or private (no ticker
// -> profile + estimated financials discovered via web search). Defaults to PUBLIC; the private
// create flow sets PRIVATE.
public enum CompanyType
{
    PUBLIC,
    PRIVATE
}
