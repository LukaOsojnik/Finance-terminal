using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddSoftDeleteToAllEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "TradeBlocs",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "RevenueSources",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "GdpSnapshots",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Events",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CountryDetails",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CountryChallenges",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CountryAdvantages",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Countries",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "CostSources",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAt",
                table: "Companies",
                type: "datetime(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "TradeBlocs");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "RevenueSources");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "GdpSnapshots");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Events");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CountryDetails");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CountryChallenges");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CountryAdvantages");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Countries");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "CostSources");

            migrationBuilder.DropColumn(
                name: "DeletedAt",
                table: "Companies");
        }
    }
}
