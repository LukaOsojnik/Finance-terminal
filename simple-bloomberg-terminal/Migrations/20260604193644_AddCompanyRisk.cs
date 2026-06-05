using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyRisk : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceFieldReview_OneSource",
                table: "SourceFieldReviews");

            migrationBuilder.AddColumn<long>(
                name: "CompanyRiskId",
                table: "SourceFieldReviews",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CompanyRisks",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Note = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DataSource = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyRisks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CompanyRisks_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_CompanyRiskId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "CompanyRiskId", "Field" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceFieldReview_OneSource",
                table: "SourceFieldReviews",
                sql: "((RevenueSourceId IS NOT NULL) + (CostSourceId IS NOT NULL) + (CompanyRiskId IS NOT NULL)) = 1");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRisks_CompanyId",
                table: "CompanyRisks",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_SourceFieldReviews_CompanyRisks_CompanyRiskId",
                table: "SourceFieldReviews",
                column: "CompanyRiskId",
                principalTable: "CompanyRisks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceFieldReviews_CompanyRisks_CompanyRiskId",
                table: "SourceFieldReviews");

            migrationBuilder.DropTable(
                name: "CompanyRisks");

            migrationBuilder.DropIndex(
                name: "IX_SourceFieldReviews_CompanyRiskId_Field",
                table: "SourceFieldReviews");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SourceFieldReview_OneSource",
                table: "SourceFieldReviews");

            migrationBuilder.DropColumn(
                name: "CompanyRiskId",
                table: "SourceFieldReviews");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SourceFieldReview_OneSource",
                table: "SourceFieldReviews",
                sql: "(RevenueSourceId IS NULL) <> (CostSourceId IS NULL)");
        }
    }
}
