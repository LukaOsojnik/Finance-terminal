using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddSourceFieldReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceFieldReviews",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    Relation = table.Column<int>(type: "int", nullable: false),
                    RevenueSourceId = table.Column<long>(type: "bigint", nullable: true),
                    CostSourceId = table.Column<long>(type: "bigint", nullable: true),
                    Field = table.Column<int>(type: "int", nullable: false),
                    Endpoint = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferencePointer = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferenceSnapshot = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReferencedValue = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Mark = table.Column<int>(type: "int", nullable: true),
                    Rationale = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ReviewedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ReviewerModel = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    DeletedAt = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceFieldReviews", x => x.Id);
                    table.CheckConstraint("CK_SourceFieldReview_OneSource", "(RevenueSourceId IS NULL) <> (CostSourceId IS NULL)");
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SourceFieldReviews_CostSources_CostSourceId",
                        column: x => x.CostSourceId,
                        principalTable: "CostSources",
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
                name: "IX_SourceFieldReviews_CostSourceId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "CostSourceId", "Field" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_RevenueSourceId_Field",
                table: "SourceFieldReviews",
                columns: new[] { "RevenueSourceId", "Field" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceFieldReviews");
        }
    }
}
