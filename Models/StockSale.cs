namespace StockPortfolio.Api.Models;

public sealed class StockSale
{
    public int Id { get; set; }
    public int StockHoldingId { get; set; }
    public StockHolding StockHolding { get; set; } = null!;
    public DateOnly SellingDate { get; set; }
    public decimal SellingQuantity { get; set; }
    public decimal SellingPrice { get; set; }
    public decimal? BuyPriceAtSale { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
