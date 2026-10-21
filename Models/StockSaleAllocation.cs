namespace StockPortfolio.Api.Models;

public sealed class StockSaleAllocation
{
    public int Id { get; set; }

    public int StockSaleId { get; set; }

    public StockSale StockSale { get; set; } = null!;

    public int StockPurchaseId { get; set; }

    public StockPurchase StockPurchase { get; set; } = null!;

    public DateOnly BuyingDate { get; set; }

    public decimal Quantity { get; set; }

    public decimal BuyingPrice { get; set; }

    public decimal BuyValue { get; set; }

    public int HoldingDays { get; set; }

    public string Term { get; set; } = "Short Term";
}