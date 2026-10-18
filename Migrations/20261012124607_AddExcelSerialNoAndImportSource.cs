using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace StockPortfolio.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddExcelSerialNoAndImportSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ExcelSerialNo",
                table: "StockPurchases",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ImportSource",
                table: "StockPurchases",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExcelSerialNo",
                table: "StockPurchases");

            migrationBuilder.DropColumn(
                name: "ImportSource",
                table: "StockPurchases");
        }
    }
}
