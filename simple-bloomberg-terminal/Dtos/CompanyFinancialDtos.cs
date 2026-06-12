using System.ComponentModel.DataAnnotations;
using simple_bloomberg_terminal.Models.Enums;

namespace simple_bloomberg_terminal.Dtos;

public record CompanyFinancialDto(
    long Id,
    long CompanyId,
    int FiscalYear,
    FiscalPeriod Period,
    DateOnly? EndDate,
    string? ReportedCurrency,
    DataSource Source,
    DateTime CapturedAt,
    double? Revenue,
    double? CostOfRevenue,
    double? GrossProfit,
    double? OperatingIncome,
    double? Ebitda,
    double? NetIncome,
    double? Eps,
    double? GrossMargin,
    double? OperatingMargin,
    double? NetMargin,
    double? CurrentRatio,
    double? DebtToEquity,
    double? TotalCash,
    double? TotalDebt,
    double? OperatingCashFlow,
    double? FreeCashFlow);

public record CompanyFinancialRequestDto
{
    [Required] public long? CompanyId { get; init; }
    [Range(1900, 2200)] public int FiscalYear { get; init; }
    [Required] public FiscalPeriod? Period { get; init; }
    public DateOnly? EndDate { get; init; }
    public string? ReportedCurrency { get; init; }
    public DataSource Source { get; init; }
    public double? Revenue { get; init; }
    public double? CostOfRevenue { get; init; }
    public double? GrossProfit { get; init; }
    public double? OperatingIncome { get; init; }
    public double? Ebitda { get; init; }
    public double? NetIncome { get; init; }
    public double? Eps { get; init; }
    public double? GrossMargin { get; init; }
    public double? OperatingMargin { get; init; }
    public double? NetMargin { get; init; }
    public double? CurrentRatio { get; init; }
    public double? DebtToEquity { get; init; }
    public double? TotalCash { get; init; }
    public double? TotalDebt { get; init; }
    public double? OperatingCashFlow { get; init; }
    public double? FreeCashFlow { get; init; }
}
