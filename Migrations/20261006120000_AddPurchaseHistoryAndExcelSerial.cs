using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Api.Migrations
{
    public partial class AddPurchaseHistoryAndExcelSerial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BuyPriceAtSale",
                table: "StockSales",
                type: "decimal(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StockPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<int>(type: "int", nullable: false),
                    StockHoldingId = table.Column<int>(type: "int", nullable: false),
                    SerialNo = table.Column<int>(type: "int", nullable: false),
                    BuyingDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BuyingQuantity = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    BuyingPrice = table.Column<decimal>(type: "decimal(18,4)", precision: 18, scale: 4, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false)
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
                        principalColumn: "Id",
                        onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockPurchases_StockHoldingId",
                table: "StockPurchases",
                column: "StockHoldingId");

            migrationBuilder.CreateIndex(
                name: "IX_StockPurchases_UserId_SerialNo",
                table: "StockPurchases",
                columns: new[] { "UserId", "SerialNo" },
                unique: true);

            // Preserve all existing holdings as one legacy purchase lot.
            // Negative serials are intentionally outside the live Excel serial range.
            migrationBuilder.Sql("""
                INSERT INTO [StockPurchases]
                    ([UserId], [StockHoldingId], [SerialNo], [BuyingDate],
                     [BuyingQuantity], [BuyingPrice], [CreatedAtUtc])
                SELECT
                    [UserId], [Id], -[Id], [BuyingDate],
                    [BuyingQuantity], [BuyingPrice], [CreatedAtUtc]
                FROM [StockHoldings];
                """);

            // Freeze the cost basis of existing sales before the holding's
            // BuyingPrice becomes a weighted average of multiple purchases.
            migrationBuilder.Sql("""
                UPDATE s
                SET s.[BuyPriceAtSale] = h.[BuyingPrice]
                FROM [StockSales] s
                INNER JOIN [StockHoldings] h
                    ON h.[Id] = s.[StockHoldingId];
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "StockPurchases");

            migrationBuilder.DropColumn(
                name: "BuyPriceAtSale",
                table: "StockSales");
        }
    }
}
