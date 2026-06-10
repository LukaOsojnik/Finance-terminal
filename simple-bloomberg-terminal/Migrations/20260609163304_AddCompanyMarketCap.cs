using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddCompanyMarketCap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MarketCap",
                table: "Companies",
                type: "double",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MarketCap",
                table: "Companies");
        }
    }
}
