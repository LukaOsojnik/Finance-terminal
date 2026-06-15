using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiProviderApiKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnthropicKey",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "KimiKey",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "OpenAiKey",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ParsingModel",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ParsingProvider",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "WebSearchModel",
                table: "UserApiKeys",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnthropicKey",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "KimiKey",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "OpenAiKey",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "ParsingModel",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "ParsingProvider",
                table: "UserApiKeys");

            migrationBuilder.DropColumn(
                name: "WebSearchModel",
                table: "UserApiKeys");
        }
    }
}
