using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddStockPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ClosingPriceFetchedAtUtc",
                table: "StockHoldings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClosingPriceSource",
                table: "StockHoldings",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "LatestTradePrice",
                table: "StockHoldings",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LatestTradePriceFetchedAtUtc",
                table: "StockHoldings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LatestTradePriceTimeUtc",
                table: "StockHoldings",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NseBhavcopyImports",
                columns: table => new
                {
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RowCount = table.Column<int>(type: "int", nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NseBhavcopyImports", x => x.PriceDate);
                });

            migrationBuilder.CreateTable(
                name: "StockClosingPrices",
                columns: table => new
                {
                    Symbol = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    Exchange = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Series = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    PriceDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosingPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    FetchedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StockClosingPrices", x => new { x.Exchange, x.Series, x.PriceDate, x.Symbol });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "NseBhavcopyImports");

            migrationBuilder.DropTable(
                name: "StockClosingPrices");

            migrationBuilder.DropColumn(
                name: "ClosingPriceFetchedAtUtc",
                table: "StockHoldings");

            migrationBuilder.DropColumn(
                name: "ClosingPriceSource",
                table: "StockHoldings");

            migrationBuilder.DropColumn(
                name: "LatestTradePrice",
                table: "StockHoldings");

            migrationBuilder.DropColumn(
                name: "LatestTradePriceFetchedAtUtc",
                table: "StockHoldings");

            migrationBuilder.DropColumn(
                name: "LatestTradePriceTimeUtc",
                table: "StockHoldings");
        }
    }
}
