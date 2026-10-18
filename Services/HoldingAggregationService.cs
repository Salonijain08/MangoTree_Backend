using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Services;

public static class HoldingAggregationService
{
    public static void Recalculate(StockHolding holding)
    {
        var purchases = holding.Purchases.ToList();
        if (purchases.Count == 0) return;

        holding.BuyingDate = purchases.Min(x => x.BuyingDate);
        holding.BuyingQuantity = purchases.Sum(x => x.BuyingQuantity);

        var totalCost = purchases.Sum(x => x.BuyingQuantity * x.BuyingPrice);
        holding.BuyingPrice = holding.BuyingQuantity == 0
            ? 0m
            : totalCost / holding.BuyingQuantity;

        holding.UpdatedAtUtc = DateTime.UtcNow;
    }
}
