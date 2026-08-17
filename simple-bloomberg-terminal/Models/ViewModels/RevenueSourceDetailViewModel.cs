using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>
/// The single revenue-source management page: the source itself (carrying its own reference,
/// evidence and source filing), an edit model for its fields (posted to the shared Edit action),
/// and the company's filings to choose from when setting the row's source filing.
/// </summary>
public class RevenueSourceDetailViewModel
{
    public required RevenueSource Source { get; set; }
    public required RevenueSourceEditModel Edit { get; set; }
    public IEnumerable<Filing> CompanyFilings { get; set; } = [];
}
