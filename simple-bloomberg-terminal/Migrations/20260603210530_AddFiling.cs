using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddFiling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "RevenueSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "CostSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Filings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    AccessionNumber = table.Column<string>(type: "varchar(255)", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Form = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FilingDate = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    PrimaryDocUrl = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Filings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Filings_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_FilingId",
                table: "RevenueSources",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_FilingId",
                table: "CostSources",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_Filings_AccessionNumber",
                table: "Filings",
                column: "AccessionNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Filings_CompanyId",
                table: "Filings",
                column: "CompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CostSources_Filings_FilingId",
                table: "CostSources",
                column: "FilingId",
                principalTable: "Filings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_RevenueSources_Filings_FilingId",
                table: "RevenueSources",
                column: "FilingId",
                principalTable: "Filings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CostSources_Filings_FilingId",
                table: "CostSources");

            migrationBuilder.DropForeignKey(
                name: "FK_RevenueSources_Filings_FilingId",
                table: "RevenueSources");

            migrationBuilder.DropTable(
                name: "Filings");

            migrationBuilder.DropIndex(
                name: "IX_RevenueSources_FilingId",
                table: "RevenueSources");

            migrationBuilder.DropIndex(
                name: "IX_CostSources_FilingId",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "CostSources");
        }
    }
}
