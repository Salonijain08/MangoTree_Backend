using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Services;

namespace StockPortfolio.Api.Helpers;

public static class HoldingMapper
{
    // =========================================================
    // SIMPLE RESPONSE
    // =========================================================

    public static HoldingResponse ToResponse(
        this StockHolding holding)
    {
        var today = MarketClock.Today;

        var quote = SavedQuote(
            holding,
            today);

        return holding.ToResponse(
            today,
            false,
            quote,
            quote?.Source == "NSE_BHAVCOPY"
                ? "FINAL"
                : quote is null
                    ? "UNAVAILABLE"
                    : "CACHED",
            null);
    }

    // =========================================================
    // SAVED PRICE
    // =========================================================

    public static PriceQuote? SavedQuote(
        StockHolding holding,
        DateOnly today)
    {
        if (
            holding.ClosingPriceSource == "NSE_BHAVCOPY" &&
            holding.ClosingPriceDate == today &&
            holding.LatestClosingPrice is > 0)
        {
            return new PriceQuote(
                holding.LatestClosingPrice.Value,
                today,
                "NSE_BHAVCOPY",
                null,
                holding.ClosingPriceFetchedAtUtc
                    ??
                    new DateTimeOffset(
                        DateTime.SpecifyKind(
                            holding.UpdatedAtUtc,
                            DateTimeKind.Utc)));
        }

        if (
            holding.LatestTradePrice is > 0 &&
            holding.LatestTradePriceTimeUtc is { } time &&
            holding.LatestTradePriceFetchedAtUtc is { } fetchedAt)
        {
            return new PriceQuote(
                holding.LatestTradePrice.Value,
                DateOnly.FromDateTime(
                    time
                        .ToOffset(
                            MarketClock.IndiaOffset)
                        .DateTime),
                "YAHOO",
                time,
                fetchedAt);
        }

        return null;
    }

    // =========================================================
    // FULL HOLDING RESPONSE
    // =========================================================

    public static HoldingResponse ToResponse(
        this StockHolding holding,
        DateOnly valuationDate,
        bool historical,
        PriceQuote? quote,
        string status,
        string? message)
    {
        // =====================================================
        // PURCHASES
        // =====================================================

        var purchases =
            holding.Purchases
                .Where(x =>
                    x.BuyingDate <= valuationDate)
                .OrderBy(x =>
                    x.BuyingDate)
                .ThenBy(x =>
                    x.SerialNo)
                .ThenBy(x =>
                    x.Id)
                .ToList();

        // =====================================================
        // LEGACY HOLDING FALLBACK
        // =====================================================

        if (
            purchases.Count == 0 &&
            holding.Purchases.Count == 0 &&
            holding.BuyingDate <= valuationDate)
        {
            purchases.Add(
                new StockPurchase
                {
                    Id = 0,
                    SerialNo = 0,
                    StockHoldingId = holding.Id,
                    BuyingDate = holding.BuyingDate,
                    BuyingQuantity = holding.BuyingQuantity,
                    BuyingPrice = holding.BuyingPrice
                });
        }

        // =====================================================
        // TOTAL PURCHASE CALCULATION
        // =====================================================

        var purchasedQuantity =
            purchases.Sum(
                x => x.BuyingQuantity);

        var investment =
            purchases.Sum(
                x =>
                    x.BuyingQuantity *
                    x.BuyingPrice);

        // =====================================================
        // SALES
        // =====================================================

        var includedSales =
            holding.Sales
                .Where(x =>
                    x.SellingDate <= valuationDate)
                .OrderBy(x =>
                    x.SellingDate)
                .ThenBy(x =>
                    x.CreatedAtUtc)
                .ThenBy(x =>
                    x.Id)
                .ToList();

        // =====================================================
        // TOTAL SOLD
        // =====================================================

        var soldQuantity =
            includedSales.Sum(
                x => x.SellingQuantity);

        // =====================================================
        // AVERAGE SOLD RATE
        // =====================================================
        //
        // Weighted average selling price:
        //
        // Total Sold Value / Total Sold Quantity
        //
        // Example:
        // 2 @ 1600
        // 2 @ 1200
        //
        // Total Sold Value = 5600
        // Total Sold Qty   = 4
        //
        // Average Sold Rate = 1400
        //
        // =====================================================

        var totalSoldValue =
            includedSales.Sum(
                x =>
                    x.SellingQuantity *
                    x.SellingPrice);

        var averageSoldRate =
            soldQuantity == 0
                ? (decimal?)null
                : totalSoldValue /
                  soldQuantity;

        var remainingQuantity =
            Math.Max(
                0m,
                purchasedQuantity -
                soldQuantity);

        // =====================================================
        // FIFO CALCULATION
        // =====================================================

        var consumedByPurchase =
            purchases.ToDictionary(
                x => x.Id,
                _ => 0m);

        // =====================================================
        // REPLAY ALL SALES USING FIFO
        // =====================================================

        foreach (
            var sale in includedSales
                .OrderBy(x =>
                    x.SellingDate)
                .ThenBy(x =>
                    x.CreatedAtUtc)
                .ThenBy(x =>
                    x.Id))
        {
            var quantityToAllocate =
                sale.SellingQuantity;

            foreach (
                var purchase in purchases)
            {
                if (
                    quantityToAllocate <= 0)
                {
                    break;
                }

                var alreadyConsumed =
                    consumedByPurchase[
                        purchase.Id];

                var availableFromPurchase =
                    Math.Max(
                        0m,
                        purchase.BuyingQuantity -
                        alreadyConsumed);

                if (
                    availableFromPurchase <= 0)
                {
                    continue;
                }

                var consume =
                    Math.Min(
                        availableFromPurchase,
                        quantityToAllocate);

                consumedByPurchase[
                    purchase.Id] += consume;

                quantityToAllocate -=
                    consume;
            }
        }

        // =====================================================
        // REMAINING COST
        // =====================================================
        //
        // This is the actual cost of the stocks that are
        // currently remaining after FIFO sales.
        //
        // Example:
        // 1 @ 1500
        // 15 @ 1300
        // 50 @ 1450
        //
        // Remaining Cost = 93500
        //
        // =====================================================

        var remainingBuyValue =
            purchases.Sum(x =>
            {
                var soldFromPurchase =
                    consumedByPurchase.TryGetValue(
                        x.Id,
                        out var sold)
                            ? sold
                            : 0m;

                var remainingFromPurchase =
                    Math.Max(
                        0m,
                        x.BuyingQuantity -
                        soldFromPurchase);

                return remainingFromPurchase *
                       x.BuyingPrice;
            });

        // =====================================================
        // REMAINING AVERAGE BUYING PRICE
        // =====================================================

        var averageBuyingPrice =
            remainingQuantity == 0
                ? 0m
                : remainingBuyValue /
                  remainingQuantity;

        // =====================================================
        // CURRENT HOLDING VALUE
        // =====================================================

        decimal? currentValue =
            remainingQuantity == 0
                ? 0m
                : quote is null
                    ? null
                    : remainingQuantity *
                      quote.Price;

        // =====================================================
        // REALIZED PROFIT
        // =====================================================
        //
        // Sale P/L remains separate.
        //
        // It does NOT get added to Total P/L below.
        //
        // =====================================================

        var realized =
            includedSales.Sum(
                x =>
                    (
                        x.SellingPrice -
                        (
                            x.BuyPriceAtSale
                            ??
                            averageBuyingPrice
                        )
                    )
                    *
                    x.SellingQuantity);

        // =====================================================
        // UNREALIZED PROFIT
        // =====================================================
        //
        // IMPORTANT:
        // Current remaining stock P/L only.
        //
        // Formula:
        //
        // Current Value - Remaining Cost
        //
        // =====================================================

        decimal? unrealized =
            remainingQuantity == 0
                ? 0m
                : quote is null
                    ? null
                    : quote.Price *
                      remainingQuantity -
                      remainingBuyValue;

        // =====================================================
        // TOTAL PROFIT & LOSS
        // =====================================================
        //
        // IMPORTANT:
        //
        // For the holdings screen, Total P/L means the
        // CURRENT / UNREALIZED P/L of the remaining stocks.
        //
        // Realized P/L is kept separately above.
        //
        // =====================================================

        decimal? total =
            unrealized;

        // =====================================================
        // PROFIT & LOSS %
        // =====================================================

        decimal? percentage =
            remainingBuyValue == 0 ||
            unrealized is null
                ? null
                : unrealized.Value /
                  remainingBuyValue *
                  100;

        // =====================================================
        // PURCHASE RESPONSE
        // =====================================================

        var purchaseResponses =
            purchases
                .Select(x =>
                {
                    // -----------------------------------------
                    // Original purchase value
                    // -----------------------------------------

                    var buyValue =
                        x.BuyingQuantity *
                        x.BuyingPrice;

                    // -----------------------------------------
                    // FIFO sold quantity
                    // -----------------------------------------

                    var soldFromPurchase =
                        consumedByPurchase.TryGetValue(
                            x.Id,
                            out var sold)
                                ? sold
                                : 0m;

                    // -----------------------------------------
                    // Remaining quantity
                    // -----------------------------------------

                    var remainingFromPurchase =
                        Math.Max(
                            0m,
                            x.BuyingQuantity -
                            soldFromPurchase);

                    // -----------------------------------------
                    // Remaining buy value
                    // -----------------------------------------

                    var remainingBuyValueForPurchase =
                        remainingFromPurchase *
                        x.BuyingPrice;

                    // -----------------------------------------
                    // Current market price
                    // -----------------------------------------

                    decimal? currentPrice =
                        quote?.Price;

                    // -----------------------------------------
                    // Current value ONLY remaining qty
                    // -----------------------------------------

                    decimal? purchaseCurrentValue =
                        currentPrice is null
                            ? null
                            : remainingFromPurchase *
                              currentPrice.Value;

                    // -----------------------------------------
                    // Unrealized P/L ONLY remaining qty
                    // -----------------------------------------

                    decimal? purchaseProfitLoss =
                        purchaseCurrentValue is null
                            ? null
                            : purchaseCurrentValue.Value -
                              remainingBuyValueForPurchase;

                    // -----------------------------------------
                    // P/L %
                    // -----------------------------------------

                    decimal? purchaseProfitLossPercentage =
                        remainingBuyValueForPurchase == 0
                            ? remainingFromPurchase == 0
                                ? 0m
                                : null
                            : purchaseProfitLoss is null
                                ? null
                                : purchaseProfitLoss.Value /
                                  remainingBuyValueForPurchase *
                                  100;

                    // -----------------------------------------
                    // PURCHASE RESPONSE
                    // -----------------------------------------

                    return new PurchaseResponse(
                        x.Id,
                        x.SerialNo,
                        x.BuyingDate,
                        x.BuyingQuantity,
                        x.BuyingPrice,
                        soldFromPurchase,
                        remainingFromPurchase,
                        buyValue,
                        remainingBuyValueForPurchase,
                        currentPrice,
                        purchaseCurrentValue,
                        purchaseProfitLoss,
                        purchaseProfitLossPercentage);
                })
                .ToArray();

        // =====================================================
        // SALE RESPONSE
        // =====================================================

        var sales =
            includedSales
                .OrderByDescending(x =>
                    x.SellingDate)
                .ThenByDescending(x =>
                    x.CreatedAtUtc)
                .ThenByDescending(x =>
                    x.Id)
                .Select(x =>
                {
                    // -----------------------------------------
                    // Buy price used for THIS sale
                    // -----------------------------------------
                    //
                    // Prefer the frozen FIFO buy price saved
                    // when the sale was created.
                    //
                    // Fallback to average buying price only
                    // for legacy sales where BuyPriceAtSale
                    // is not available.
                    //
                    // -----------------------------------------

                    var buyPrice =
                        x.BuyPriceAtSale
                        ??
                        averageBuyingPrice;

                    // -----------------------------------------
                    // Buy value of sold quantity
                    // -----------------------------------------

                    var buyValue =
                        x.SellingQuantity *
                        buyPrice;

                    // -----------------------------------------
                    // Sale value
                    // -----------------------------------------

                    var saleValue =
                        x.SellingQuantity *
                        x.SellingPrice;

                    // -----------------------------------------
                    // Realized P/L
                    // -----------------------------------------

                    var realizedProfitLoss =
                        saleValue -
                        buyValue;

                    // -----------------------------------------
                    // SALE RESPONSE
                    // -----------------------------------------

                    return new SaleResponse(
                        x.Id,
                        x.SellingDate,
                        x.SellingQuantity,
                        x.SellingPrice,
                        buyPrice,
                        buyValue,
                        saleValue,
                        realizedProfitLoss);
                })
                .ToArray();

        // =====================================================
        // BUYING DATE
        // =====================================================

        var buyingDate =
            purchases.Count == 0
                ? holding.BuyingDate
                : purchases.Min(
                    x => x.BuyingDate);

        // =====================================================
        // FINAL RESPONSE
        // =====================================================

        return new HoldingResponse(
            holding.Id,
            holding.Symbol,
            holding.Exchange,
            holding.CompanyName,
            holding.InstrumentKey,
            buyingDate,

            // Main holding quantity = REMAINING qty
            remainingQuantity,

            // Main holding buying price = average price
            // of REMAINING stocks
            averageBuyingPrice,

            soldQuantity,
            remainingQuantity,

            holding.ClosingPriceSource ==
                "NSE_BHAVCOPY"
                ? holding.LatestClosingPrice
                : null,

            holding.ClosingPriceSource ==
                "NSE_BHAVCOPY"
                ? holding.ClosingPriceDate
                : null,

            // Original total investment
            investment,

            // Actual remaining cost
            remainingBuyValue,

            currentValue,

            // Realized P/L remains separate
            realized,

            // Current/unrealized P/L
            unrealized,

            // IMPORTANT:
            // Total P/L = Unrealized P/L
            total,

            // IMPORTANT:
            // P/L % = Unrealized P/L / Remaining Cost
            percentage,

            remainingQuantity == 0,
            sales)
        {
            // =================================================
            // PURCHASES
            // =================================================

            Purchases =
                purchaseResponses,

            // =================================================
            // AVERAGE SOLD RATE
            // =================================================
            //
            // Weighted average of all sold transactions.
            //
            // Example:
            // 2 @ 1600 + 2 @ 1200
            // = Average Sold Rate ₹1400
            //
            // =================================================

            AverageSoldRate =
                averageSoldRate,

            // =================================================
            // PRICE INFORMATION
            // =================================================

            LatestTradePrice =
                holding.LatestTradePrice,

            LatestTradePriceTimeUtc =
                holding.LatestTradePriceTimeUtc,

            LatestTradePriceFetchedAtUtc =
                holding.LatestTradePriceFetchedAtUtc,

            DisplayPrice =
                quote?.Price,

            PriceSource =
                quote?.Source ??
                "UNAVAILABLE",

            PriceDate =
                quote?.Date,

            PriceTimeUtc =
                quote?.QuoteTimeUtc,

            FetchedAtUtc =
                quote?.FetchedAtUtc,

            ValuationDate =
                valuationDate,

            PriceStatus =
                status,

            PriceMessage =
                message,

            IsHistorical =
                historical
        };
    }
}