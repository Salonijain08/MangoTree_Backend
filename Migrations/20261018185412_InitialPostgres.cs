using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace StockPortfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialPostgres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NseBhavcopyImports",
                columns: table => new
                {
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RowCount = table.Column<int>(type: "integer", nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NseBhavcopyImports", x => x.PriceDate);
                });

            migrationBuilder.CreateTable(
                name: "StockClosingPrices",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Exchange = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Series = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockClosingPrices", x => new { x.Exchange, x.Series, x.PriceDate, x.Symbol });
                });

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FullName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StockHoldings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Symbol = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Exchange = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    CompanyName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    InstrumentKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BuyingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BuyingQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BuyingPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    LatestTradePrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    LatestTradePriceTimeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatestTradePriceFetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LatestClosingPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    ClosingPriceDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ClosingPriceFetchedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosingPriceSource = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockHoldings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockHoldings_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StockPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    StockHoldingId = table.Column<int>(type: "integer", nullable: false),
                    SerialNo = table.Column<int>(type: "integer", nullable: false),
                    ExcelSerialNo = table.Column<int>(type: "integer", nullable: true),
                    ImportSource = table.Column<string>(type: "text", nullable: false),
                    BuyingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BuyingQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BuyingPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockPurchases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockPurchases_StockHoldings_StockHoldingId",
                        column: x => x.StockHoldingId,
                        principalTable: "StockHoldings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_StockPurchases_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "StockSales",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    StockHoldingId = table.Column<int>(type: "integer", nullable: false),
                    SellingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    SellingQuantity = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    SellingPrice = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: false),
                    BuyPriceAtSale = table.Column<decimal>(type: "numeric(18,4)", precision: 18, scale: 4, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockSales", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StockSales_StockHoldings_StockHoldingId",
                        column: x => x.StockHoldingId,
                        principalTable: "StockHoldings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockHoldings_UserId",
                table: "StockHoldings",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_StockPurchases_StockHoldingId",
                table: "StockPurchases",
                column: "StockHoldingId");

            migrationBuilder.CreateIndex(
                name: "IX_StockPurchases_UserId_SerialNo",
                table: "StockPurchases",
                columns: new[] { "UserId", "SerialNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StockSales_StockHoldingId",
                table: "StockSales",
                column: "StockHoldingId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NseBhavcopyImports");

            migrationBuilder.DropTable(
                name: "StockClosingPrices");

            migrationBuilder.DropTable(
                name: "StockPurchases");

            migrationBuilder.DropTable(
                name: "StockSales");

            migrationBuilder.DropTable(
                name: "StockHoldings");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
