using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddContributionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContributedByUserId",
                table: "RevenueSources",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "RevenueSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SupersedesId",
                table: "RevenueSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributedByUserId",
                table: "CostSources",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CostSources",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SupersedesId",
                table: "CostSources",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContributedByUserId",
                table: "CompanyRisks",
                type: "varchar(255)",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "CompanyRisks",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<long>(
                name: "SupersedesId",
                table: "CompanyRisks",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_ContributedByUserId",
                table: "RevenueSources",
                column: "ContributedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_ContributedByUserId",
                table: "CostSources",
                column: "ContributedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyRisks_ContributedByUserId",
                table: "CompanyRisks",
                column: "ContributedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyRisks_AspNetUsers_ContributedByUserId",
                table: "CompanyRisks",
                column: "ContributedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_CostSources_AspNetUsers_ContributedByUserId",
                table: "CostSources",
                column: "ContributedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_RevenueSources_AspNetUsers_ContributedByUserId",
                table: "RevenueSources",
                column: "ContributedByUserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyRisks_AspNetUsers_ContributedByUserId",
                table: "CompanyRisks");

            migrationBuilder.DropForeignKey(
                name: "FK_CostSources_AspNetUsers_ContributedByUserId",
                table: "CostSources");

            migrationBuilder.DropForeignKey(
                name: "FK_RevenueSources_AspNetUsers_ContributedByUserId",
                table: "RevenueSources");

            migrationBuilder.DropIndex(
                name: "IX_RevenueSources_ContributedByUserId",
                table: "RevenueSources");

            migrationBuilder.DropIndex(
                name: "IX_CostSources_ContributedByUserId",
                table: "CostSources");

            migrationBuilder.DropIndex(
                name: "IX_CompanyRisks_ContributedByUserId",
                table: "CompanyRisks");

            migrationBuilder.DropColumn(
                name: "ContributedByUserId",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "SupersedesId",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "ContributedByUserId",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "SupersedesId",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "ContributedByUserId",
                table: "CompanyRisks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "CompanyRisks");

            migrationBuilder.DropColumn(
                name: "SupersedesId",
                table: "CompanyRisks");
        }
    }
}
