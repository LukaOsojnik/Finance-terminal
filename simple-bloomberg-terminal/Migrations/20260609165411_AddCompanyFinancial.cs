using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyFinancial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CompanyFinancials",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    FiscalYear = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ReportedCurrency = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Source = table.Column<int>(type: "int", nullable: false),
                    CapturedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Revenue = table.Column<double>(type: "double", nullable: true),
                    CostOfRevenue = table.Column<double>(type: "double", nullable: true),
                    GrossProfit = table.Column<double>(type: "double", nullable: true),
                    OperatingIncome = table.Column<double>(type: "double", nullable: true),
                    Ebitda = table.Column<double>(type: "double", nullable: true),
                    NetIncome = table.Column<double>(type: "double", nullable: true),
                    Eps = table.Column<double>(type: "double", nullable: true),
                    GrossMargin = table.Column<double>(type: "double", nullable: true),
                    OperatingMargin = table.Column<double>(type: "double", nullable: true),
                    NetMargin = table.Column<double>(type: "double", nullable: true),
                    CurrentRatio = table.Column<double>(type: "double", nullable: true),
                    DebtToEquity = table.Column<double>(type: "double", nullable: true),
                    TotalCash = table.Column<double>(type: "double", nullable: true),
                    TotalDebt = table.Column<double>(type: "double", nullable: true),
                    OperatingCashFlow = table.Column<double>(type: "double", nullable: true),
                    FreeCashFlow = table.Column<double>(type: "double", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyFinancials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyFinancials_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyFinancials_CompanyId_FiscalYear_Period",
                table: "CompanyFinancials",
                columns: new[] { "CompanyId", "FiscalYear", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyFinancials");
        }
    }
}
