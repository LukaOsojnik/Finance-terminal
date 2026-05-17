using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

public class RevenueSource
{
    public RevenueSource(SourceType sourceType, string name, long companyId)
    {
        SourceType = sourceType;
        Name = name;
        CompanyId = companyId;
    }

    [Key]
    public long Id { get; set; }
    public SourceType SourceType { get; set; }
    public string Name { get; set; }
    public double? Value { get; set; }
    public double? Percentage { get; set; }
    public DataSource? DataSource { get; set; }
    public long CompanyId { get; set; }
    public long? RelatedCompanyId { get; set; }
    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }

    [ForeignKey("RelatedCompanyId")]
    public virtual Company? RelatedCompany { get; set; }
}
