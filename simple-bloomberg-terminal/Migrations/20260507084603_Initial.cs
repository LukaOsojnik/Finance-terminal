using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace simple_bloomberg_terminal.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Countries",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Code = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Region = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CurrencyCode = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    GdpUsd = table.Column<double>(type: "double", nullable: true),
                    Population = table.Column<long>(type: "bigint", nullable: true),
                    RiskRating = table.Column<double>(type: "double", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Countries", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Events",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Title = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ImpactScore = table.Column<double>(type: "double", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Events", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "TradeBlocs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Code = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Description = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    FoundedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeBlocs", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Companies",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Cik = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Sector = table.Column<int>(type: "int", nullable: false),
                    Industry = table.Column<int>(type: "int", nullable: true),
                    RevenueTotal = table.Column<double>(type: "double", nullable: true),
                    GrossMargin = table.Column<double>(type: "double", nullable: true),
                    AsOf = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Companies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Companies_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CountryDetails",
                columns: table => new
                {
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    MarketPosition = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryDetails", x => x.CountryId);
                    table.ForeignKey(
                        name: "FK_CountryDetails_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CountryEvent",
                columns: table => new
                {
                    CountriesId = table.Column<long>(type: "bigint", nullable: false),
                    EventsId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryEvent", x => new { x.CountriesId, x.EventsId });
                    table.ForeignKey(
                        name: "FK_CountryEvent_Countries_CountriesId",
                        column: x => x.CountriesId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountryEvent_Events_EventsId",
                        column: x => x.EventsId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CountryTradeBloc",
                columns: table => new
                {
                    CountriesId = table.Column<long>(type: "bigint", nullable: false),
                    TradeBlocsId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryTradeBloc", x => new { x.CountriesId, x.TradeBlocsId });
                    table.ForeignKey(
                        name: "FK_CountryTradeBloc_Countries_CountriesId",
                        column: x => x.CountriesId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountryTradeBloc_TradeBlocs_TradeBlocsId",
                        column: x => x.TradeBlocsId,
                        principalTable: "TradeBlocs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventTradeBloc",
                columns: table => new
                {
                    EventsId = table.Column<long>(type: "bigint", nullable: false),
                    TradeBlocsId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventTradeBloc", x => new { x.EventsId, x.TradeBlocsId });
                    table.ForeignKey(
                        name: "FK_EventTradeBloc_Events_EventsId",
                        column: x => x.EventsId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventTradeBloc_TradeBlocs_TradeBlocsId",
                        column: x => x.TradeBlocsId,
                        principalTable: "TradeBlocs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CompanyEvent",
                columns: table => new
                {
                    CompaniesId = table.Column<long>(type: "bigint", nullable: false),
                    EventsId = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CompanyEvent", x => new { x.CompaniesId, x.EventsId });
                    table.ForeignKey(
                        name: "FK_CompanyEvent_Companies_CompaniesId",
                        column: x => x.CompaniesId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CompanyEvent_Events_EventsId",
                        column: x => x.EventsId,
                        principalTable: "Events",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CostSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CostBase = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<double>(type: "double", nullable: true),
                    Percentage = table.Column<double>(type: "double", nullable: true),
                    DataSource = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    RelatedCompanyId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CostSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CostSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CostSources_Companies_RelatedCompanyId",
                        column: x => x.RelatedCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RevenueSources",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    SourceType = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Value = table.Column<double>(type: "double", nullable: true),
                    Percentage = table.Column<double>(type: "double", nullable: true),
                    DataSource = table.Column<int>(type: "int", nullable: true),
                    CompanyId = table.Column<long>(type: "bigint", nullable: false),
                    RelatedCompanyId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RevenueSources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RevenueSources_Companies_CompanyId",
                        column: x => x.CompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RevenueSources_Companies_RelatedCompanyId",
                        column: x => x.RelatedCompanyId,
                        principalTable: "Companies",
                        principalColumn: "Id");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CountryAdvantages",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryDetailsCountryId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryAdvantages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CountryAdvantages_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountryAdvantages_CountryDetails_CountryDetailsCountryId",
                        column: x => x.CountryDetailsCountryId,
                        principalTable: "CountryDetails",
                        principalColumn: "CountryId");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CountryChallenges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Text = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CountryDetailsCountryId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CountryChallenges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CountryChallenges_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CountryChallenges_CountryDetails_CountryDetailsCountryId",
                        column: x => x.CountryDetailsCountryId,
                        principalTable: "CountryDetails",
                        principalColumn: "CountryId");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "GdpSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CountryId = table.Column<long>(type: "bigint", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    GdpUsd = table.Column<double>(type: "double", nullable: false),
                    CountryDetailsCountryId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GdpSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GdpSnapshots_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GdpSnapshots_CountryDetails_CountryDetailsCountryId",
                        column: x => x.CountryDetailsCountryId,
                        principalTable: "CountryDetails",
                        principalColumn: "CountryId");
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Companies_CountryId",
                table: "Companies",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CompanyEvent_EventsId",
                table: "CompanyEvent",
                column: "EventsId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_CompanyId",
                table: "CostSources",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CostSources_RelatedCompanyId",
                table: "CostSources",
                column: "RelatedCompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryAdvantages_CountryDetailsCountryId",
                table: "CountryAdvantages",
                column: "CountryDetailsCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryAdvantages_CountryId",
                table: "CountryAdvantages",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryChallenges_CountryDetailsCountryId",
                table: "CountryChallenges",
                column: "CountryDetailsCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryChallenges_CountryId",
                table: "CountryChallenges",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryEvent_EventsId",
                table: "CountryEvent",
                column: "EventsId");

            migrationBuilder.CreateIndex(
                name: "IX_CountryTradeBloc_TradeBlocsId",
                table: "CountryTradeBloc",
                column: "TradeBlocsId");

            migrationBuilder.CreateIndex(
                name: "IX_EventTradeBloc_TradeBlocsId",
                table: "EventTradeBloc",
                column: "TradeBlocsId");

            migrationBuilder.CreateIndex(
                name: "IX_GdpSnapshots_CountryDetailsCountryId",
                table: "GdpSnapshots",
                column: "CountryDetailsCountryId");

            migrationBuilder.CreateIndex(
                name: "IX_GdpSnapshots_CountryId",
                table: "GdpSnapshots",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_CompanyId",
                table: "RevenueSources",
                column: "CompanyId");

            migrationBuilder.CreateIndex(
                name: "IX_RevenueSources_RelatedCompanyId",
                table: "RevenueSources",
                column: "RelatedCompanyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CompanyEvent");

            migrationBuilder.DropTable(
                name: "CostSources");

            migrationBuilder.DropTable(
                name: "CountryAdvantages");

            migrationBuilder.DropTable(
                name: "CountryChallenges");

            migrationBuilder.DropTable(
                name: "CountryEvent");

            migrationBuilder.DropTable(
                name: "CountryTradeBloc");

            migrationBuilder.DropTable(
                name: "EventTradeBloc");

            migrationBuilder.DropTable(
                name: "GdpSnapshots");

            migrationBuilder.DropTable(
                name: "RevenueSources");

            migrationBuilder.DropTable(
                name: "Events");

            migrationBuilder.DropTable(
                name: "TradeBlocs");

            migrationBuilder.DropTable(
                name: "CountryDetails");

            migrationBuilder.DropTable(
                name: "Companies");

            migrationBuilder.DropTable(
                name: "Countries");
        }
    }
}
