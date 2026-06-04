using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class MoveFilingLinkToReview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Add the new per-review filing link first.
            migrationBuilder.AddColumn<long>(
                name: "FilingId",
                table: "SourceFieldReviews",
                type: "bigint",
                nullable: true);

            // 2. Backfill: carry each source's old single filing onto its reviews, so existing
            //    proof links survive the move (each cited cell inherits the source's filing).
            migrationBuilder.Sql(
                "UPDATE SourceFieldReviews r JOIN RevenueSources s ON r.RevenueSourceId = s.Id " +
                "SET r.FilingId = s.FilingId WHERE s.FilingId IS NOT NULL;");
            migrationBuilder.Sql(
                "UPDATE SourceFieldReviews r JOIN CostSources s ON r.CostSourceId = s.Id " +
                "SET r.FilingId = s.FilingId WHERE s.FilingId IS NOT NULL;");

            migrationBuilder.CreateIndex(
                name: "IX_SourceFieldReviews_FilingId",
                table: "SourceFieldReviews",
                column: "FilingId");

            migrationBuilder.AddForeignKey(
                name: "FK_SourceFieldReviews_Filings_FilingId",
                table: "SourceFieldReviews",
                column: "FilingId",
                principalTable: "Filings",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // 3. Now drop the old per-source filing link.
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

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "CostSources");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceFieldReviews_Filings_FilingId",
                table: "SourceFieldReviews");

            migrationBuilder.DropIndex(
                name: "IX_SourceFieldReviews_FilingId",
                table: "SourceFieldReviews");

            migrationBuilder.DropColumn(
                name: "FilingId",
                table: "SourceFieldReviews");

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

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_FilingId",
                table: "RevenueSources",
                column: "FilingId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_FilingId",
                table: "CostSources",
                column: "FilingId");

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
    }
}
