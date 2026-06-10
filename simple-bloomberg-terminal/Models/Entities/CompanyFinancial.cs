using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Models.Entities;

/// <summary>
/// One company's reported financials for a single fiscal period (a year FY, or a quarter Q1–Q4),
/// assembled from FMP's income / ratios / balance-sheet / cash-flow statements keyed by
/// (FiscalYear, Period). Unlike <see cref="Company"/> — which keeps only the latest denormalized
/// snapshot — this is the dated history: every fetch upserts these rows so the Details page can
/// show revenue/margins/etc. over time and which fiscal period each figure is from.
///
/// Every figure is nullable: FMP fills all of them for US filers, but the Yahoo non-US fallback
/// only supplies Revenue + NetIncome (annual), so most columns stay null for foreign companies.
/// </summary>
public class CompanyFinancial
{
    public CompanyFinancial(long companyId, int fiscalYear, FiscalPeriod period)
    {
        CompanyId = companyId;
        FiscalYear = fiscalYear;
        Period = period;
    }

    [Key]
    public long Id { get; set; }

    public long CompanyId { get; set; }
    public int FiscalYear { get; set; }
    public FiscalPeriod Period { get; set; }
    public DateOnly? EndDate { get; set; }
    public string? ReportedCurrency { get; set; }
    public DataSource Source { get; set; }
    public DateTime CapturedAt { get; set; }

    // Income statement
    public double? Revenue { get; set; }
    public double? CostOfRevenue { get; set; }
    public double? GrossProfit { get; set; }
    public double? OperatingIncome { get; set; }
    public double? Ebitda { get; set; }
    public double? NetIncome { get; set; }
    public double? Eps { get; set; }

    // Ratios (margins as 0–1 ratios, mirroring Company.GrossMargin)
    public double? GrossMargin { get; set; }
    public double? OperatingMargin { get; set; }
    public double? NetMargin { get; set; }
    public double? CurrentRatio { get; set; }
    public double? DebtToEquity { get; set; }

    // Balance sheet
    public double? TotalCash { get; set; }
    public double? TotalDebt { get; set; }

    // Cash flow
    public double? OperatingCashFlow { get; set; }
    public double? FreeCashFlow { get; set; }

    public DateTime? DeletedAt { get; set; }

    [ForeignKey("CompanyId")]
    public virtual Company? Company { get; set; }
}
