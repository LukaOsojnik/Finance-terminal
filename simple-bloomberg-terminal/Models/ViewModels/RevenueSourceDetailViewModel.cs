using simple_bloomberg_terminal.Models.Entities;

namespace simple_bloomberg_terminal.Models.ViewModels;

/// <summary>
/// The single revenue-source management page: the source itself, an edit model for its fields
/// (posted to the shared Edit action), its per-field proof reviews (each with its filing), and the
/// company's filings to choose from when replacing a field's proof filing.
/// </summary>
public class RevenueSourceDetailViewModel
{
    public required RevenueSource Source { get; set; }
    public required RevenueSourceEditModel Edit { get; set; }
    public IEnumerable<SourceFieldReview> Reviews { get; set; } = [];
    public IEnumerable<Filing> CompanyFilings { get; set; } = [];
}
