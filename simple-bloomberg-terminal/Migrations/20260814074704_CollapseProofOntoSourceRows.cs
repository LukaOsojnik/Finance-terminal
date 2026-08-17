using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class CollapseProofOntoSourceRows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the per-row proof columns (Evidence + the filing link) on all three source
            //    tables FIRST, so the backfill below has somewhere to write.
            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "RevenueSources",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "RevenueSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "CostSources",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "CostSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Evidence",
                table: "CompanyRisks",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "CompanyRisks",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_FilingId",
                table: "RevenueSources",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_FilingId",
                table: "CostSources",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRisks_FilingId",
                table: "CompanyRisks",
                column: "FilingId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyRisks_Filings_FilingId",
                table: "CompanyRisks",
                column: "FilingId",
                principalTable: "Filings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

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

            // 2. Backfill: each source row takes its single surviving review (lowest live Id) and
            //    inherits that review's snapshot as its Evidence and its filing link. The row's own
            //    Reference is left alone — it already holds the per-record source passage.
            foreach (var (table, fk) in new[]
                     {
                         ("RevenueSources", "RevenueSourceId"),
                         ("CostSources", "CostSourceId"),
                         ("CompanyRisks", "CompanyRiskId")
                     })
            {
                migrationBuilder.Sql(
                    $"UPDATE {table} s " +
                    $"JOIN (SELECT {fk} AS SourceId, MIN(Id) AS Id FROM SourceFieldReviews " +
                    $"      WHERE {fk} IS NOT NULL AND DeletedAt IS NULL " +
                    $"      GROUP BY {fk}) p ON p.SourceId = s.Id " +
                    "JOIN SourceFieldReviews r ON r.Id = p.Id " +
                    "SET s.Evidence = r.ReferenceSnapshot, s.FilingId = r.FilingId;");
            }

            // 3. Only now is the per-field table dead weight.
            migrationBuilder.DropTable(
                name: "SourceFieldReviews");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // 1. Recreate the per-field proof table first, so the row proof can be moved back into it
            //    before the columns holding it are dropped.
            migrationBuilder.CreateTable(
                name: "SourceFieldReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    CompanyRiskId = table.Column<long>(type: "bigint", nullable: true),
                    CostSourceId = table.Column<long>(type: "bigint", nullable: true),
                    FilingId = table.Column<long>(type: "bigint", nullable: true),
                    RevenueSourceId = table.Column<long>(type: "bigint", nullable: true),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Endpoint = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Field = table.Column<int>(type: "int", nullable: false),
                    Mark = table.Column<int>(type: "int", nullable: true),
                    Rationale = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferencePointer = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferenceSnapshot = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferencedValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Relation = table.Column<int>(type: "int", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewerModel = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceFieldReviews", x => x.Id);
                    table.CheckConstraint("CK_SourceFieldReview_OneSource", "((RevenueSourceId IS NOT NULL) + (CostSourceId IS NOT NULL) + (CompanyRiskId IS NOT NULL)) = 1");
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_CompanyRisks_CompanyRiskId",
                        column: x => x.CompanyRiskId,
                        principalTable: "CompanyRisks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_CostSources_CostSourceId",
                        column: x => x.CostSourceId,
                        principalTable: "CostSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_Filings_FilingId",
                        column: x => x.FilingId,
                        principalTable: "Filings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_RevenueSources_RevenueSourceId",
                        column: x => x.RevenueSourceId,
                        principalTable: "RevenueSources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_CompanyId",
                table: "SourceFieldReviews",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_CompanyRiskId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "CompanyRiskId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_CostSourceId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "CostSourceId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_FilingId",
                table: "SourceFieldReviews",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_RevenueSourceId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "RevenueSourceId", "Field" },
                unique: true);

            // 2. Reverse backfill: every source row carrying proof gets one review row back, on the
            //    VALUE field (Field=0) — the collapse lost which cell the quote backed, so the single
            //    surviving quote is restored against one cell rather than fanned back out. Relation
            //    matches the source table (COST=0, REVENUE=1, RISK=2). Endpoint/ReferencePointer are
            //    NOT NULL and no longer stored, so they come back empty.
            foreach (var (table, fk, relation) in new[]
                     {
                         ("RevenueSources", "RevenueSourceId", 1),
                         ("CostSources", "CostSourceId", 0),
                         ("CompanyRisks", "CompanyRiskId", 2)
                     })
            {
                migrationBuilder.Sql(
                    "INSERT INTO SourceFieldReviews " +
                    $"(CompanyId, Relation, {fk}, Field, Endpoint, ReferencePointer, ReferenceSnapshot, FilingId) " +
                    $"SELECT s.CompanyId, {relation}, s.Id, 0, '', '', COALESCE(s.Evidence, ''), s.FilingId " +
                    $"FROM {table} s " +
                    "WHERE s.Evidence IS NOT NULL OR s.FilingId IS NOT NULL;");
            }

            // 3. Now the per-row proof columns can go.
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyRisks_Filings_FilingId",
                table: "CompanyRisks");

            migrationBuilder.DropForeignKey(
                name: "FK_CostSources_Filings_FilingId",
                table: "CostSources");

            migrationBuilder.DropForeignKey(
                name: "FK_RevenueSources_Filings_FilingId",
                table: "RevenueSources");

            migrationBuilder.DropIndex(
                name: "IX_RevenueSources_FilingId",
                table: "RevenueSources");

            migrationBuilder.DropIndex(
                name: "IX_CostSources_FilingId",
                table: "CostSources");

            migrationBuilder.DropIndex(
                name: "IX_CompanyRisks_FilingId",
                table: "CompanyRisks");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "CompanyRisks");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "CompanyRisks");
        }
    }
}
