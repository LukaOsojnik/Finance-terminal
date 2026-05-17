using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.ViewModels;

public class RevenueSourceCreateModel
{
    [Required]
    public SourceType SourceType { get; set; }

    [Required, StringLength(160)]
    public string Name { get; set; } = string.Empty;

    public double? Value { get; set; }

    [Range(0, 100)]
    public double? Percentage { get; set; }

    public DataSource? DataSource { get; set; }

    [Required]
    public long CompanyId { get; set; }

    public long? RelatedCompanyId { get; set; }
}

public class RevenueSourceEditModel : RevenueSourceCreateModel
{
    public long Id { get; set; }
}
