using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddStockIndexSectorRegionSize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "StockIndices",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "Sector",
                table: "StockIndices",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TotalMarketCap",
                table: "StockIndices",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Region",
                table: "StockIndices");

            migrationBuilder.DropColumn(
                name: "Sector",
                table: "StockIndices");

            migrationBuilder.DropColumn(
                name: "TotalMarketCap",
                table: "StockIndices");
        }
    }
}
