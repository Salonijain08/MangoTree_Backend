using System.ComponentModel.DataAnnotations;

namespace StockPortfolio.Api.Models;

public sealed class StockHolding
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    [MaxLength(30)] public string Symbol { get; set; } = string.Empty;
    [MaxLength(10)] public string Exchange { get; set; } = "NSE";
    [MaxLength(120)] public string? CompanyName { get; set; }
    [MaxLength(100)] public string? InstrumentKey { get; set; }
    public DateOnly BuyingDate { get; set; }
    public decimal BuyingQuantity { get; set; }
    public decimal BuyingPrice { get; set; }
    public decimal? LatestTradePrice { get; set; }
    public DateTimeOffset? LatestTradePriceTimeUtc { get; set; }
    public DateTimeOffset? LatestTradePriceFetchedAtUtc { get; set; }
    public decimal? LatestClosingPrice { get; set; }
    public DateOnly? ClosingPriceDate { get; set; }
    public DateTimeOffset? ClosingPriceFetchedAtUtc { get; set; }
    [MaxLength(30)] public string? ClosingPriceSource { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<StockPurchase> Purchases { get; set; } = new List<StockPurchase>();
    public ICollection<StockSale> Sales { get; set; } = new List<StockSale>();
}
