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
        var today =
            MarketClock.Today;

        var quote =
            SavedQuote(
                holding,
                today
            );

        return holding.ToResponse(
            today,
            false,
            quote,
            quote?.Source == "NSE_BHAVCOPY"
                ? "FINAL"
                : quote is null
                    ? "UNAVAILABLE"
                    : "CACHED",
            null
        );
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
                        DateTimeKind.Utc
                    )
                )
            );
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
                            MarketClock.IndiaOffset
                        )
                        .DateTime
                ),

                "YAHOO",

                time,

                fetchedAt
            );
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
                    x.BuyingDate <= valuationDate
                )
                .OrderBy(x =>
                    x.BuyingDate
                )
                .ThenBy(x =>
                    x.SerialNo
                )
                .ThenBy(x =>
                    x.Id
                )
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

                    StockHoldingId =
                        holding.Id,

                    BuyingDate =
                        holding.BuyingDate,

                    BuyingQuantity =
                        holding.BuyingQuantity,

                    BuyingPrice =
                        holding.BuyingPrice
                }
            );
        }


        // =====================================================
        // PURCHASE LOOKUP
        // =====================================================

        var purchasesById =
            purchases.ToDictionary(
                x => x.Id,
                x => x
            );


        // =====================================================
        // TOTAL PURCHASE CALCULATION
        // =====================================================

        var purchasedQuantity =
            purchases.Sum(
                x =>
                    x.BuyingQuantity
            );


        var investment =
            purchases.Sum(
                x =>
                    x.BuyingQuantity *
                    x.BuyingPrice
            );


        // =====================================================
        // SALES
        // =====================================================

        var includedSales =
            holding.Sales
                .Where(x =>
                    x.SellingDate <= valuationDate
                )
                .OrderBy(x =>
                    x.SellingDate
                )
                .ThenBy(x =>
                    x.CreatedAtUtc
                )
                .ThenBy(x =>
                    x.Id
                )
                .ToList();


        // =====================================================
        // TOTAL SOLD
        // =====================================================

        var soldQuantity =
            includedSales.Sum(
                x =>
                    x.SellingQuantity
            );


        // =====================================================
        // AVERAGE SOLD RATE
        // =====================================================

        var totalSoldValue =
            includedSales.Sum(
                x =>
                    x.SellingQuantity *
                    x.SellingPrice
            );


        var averageSoldRate =
            soldQuantity == 0
                ? (decimal?)null
                : totalSoldValue /
                  soldQuantity;


        var remainingQuantity =
            Math.Max(
                0m,
                purchasedQuantity -
                soldQuantity
            );


        // =====================================================
        // FIFO CALCULATION
        // =====================================================

        var consumedByPurchase =
            purchases.ToDictionary(
                x => x.Id,
                _ => 0m
            );


        // =====================================================
        // LEGACY / REPLAY FIFO ALLOCATIONS
        //
        // This is important for old sales created BEFORE
        // StockSaleAllocation table existed.
        //
        // New sales will have database allocations.
        // Old sales will still receive correct FIFO allocation
        // details through this replay.
        // =====================================================

        var replayAllocationsBySale =
            new Dictionary<
                int,
                IReadOnlyCollection<SaleAllocationResponse>
            >();


        // =====================================================
        // REPLAY ALL SALES USING FIFO
        // =====================================================

        foreach (
            var sale in includedSales
                .OrderBy(x =>
                    x.SellingDate
                )
                .ThenBy(x =>
                    x.CreatedAtUtc
                )
                .ThenBy(x =>
                    x.Id
                ))
        {
            var quantityToAllocate =
                sale.SellingQuantity;


            var saleAllocations =
                new List<SaleAllocationResponse>();


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
                        purchase.Id
                    ];


                var availableFromPurchase =
                    Math.Max(
                        0m,
                        purchase.BuyingQuantity -
                        alreadyConsumed
                    );


                if (
                    availableFromPurchase <= 0)
                {
                    continue;
                }


                var consume =
                    Math.Min(
                        availableFromPurchase,
                        quantityToAllocate
                    );


                var buyValue =
                    consume *
                    purchase.BuyingPrice;


                var holdingDays =
                    sale.SellingDate.DayNumber -
                    purchase.BuyingDate.DayNumber;


                var term =
                    holdingDays > 365
                        ? "Long Term"
                        : "Short Term";


                saleAllocations.Add(
                    new SaleAllocationResponse(
                        purchase.Id,
                        purchase.SerialNo,
                        purchase.BuyingDate,
                        consume,
                        purchase.BuyingPrice,
                        buyValue,
                        holdingDays,
                        term
                    )
                );


                consumedByPurchase[
                    purchase.Id
                ] += consume;


                quantityToAllocate -=
                    consume;
            }


            replayAllocationsBySale[
                sale.Id
            ] = saleAllocations;
        }


        // =====================================================
        // REMAINING COST
        // =====================================================

        var remainingBuyValue =
            purchases.Sum(
                x =>
                {
                    var soldFromPurchase =
                        consumedByPurchase
                            .TryGetValue(
                                x.Id,
                                out var sold
                            )
                                ? sold
                                : 0m;


                    var remainingFromPurchase =
                        Math.Max(
                            0m,
                            x.BuyingQuantity -
                            soldFromPurchase
                        );


                    return remainingFromPurchase *
                           x.BuyingPrice;
                }
            );


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

        var realized =
            includedSales.Sum(
                sale =>
                {
                    decimal saleBuyValue;


                    // -----------------------------------------
                    // Prefer saved FIFO allocations
                    // -----------------------------------------

                    if (
                        sale.Allocations is not null &&
                        sale.Allocations.Count > 0)
                    {
                        saleBuyValue =
                            sale.Allocations.Sum(
                                x =>
                                    x.BuyValue
                            );
                    }

                    // -----------------------------------------
                    // Legacy sale fallback
                    // -----------------------------------------

                    else if (
                        replayAllocationsBySale
                            .TryGetValue(
                                sale.Id,
                                out var replayAllocations
                            )
                        &&
                        replayAllocations.Count > 0)
                    {
                        saleBuyValue =
                            replayAllocations.Sum(
                                x =>
                                    x.BuyValue
                            );
                    }

                    // -----------------------------------------
                    // Final legacy fallback
                    // -----------------------------------------

                    else
                    {
                        var buyPrice =
                            sale.BuyPriceAtSale
                            ??
                            averageBuyingPrice;


                        saleBuyValue =
                            sale.SellingQuantity *
                            buyPrice;
                    }


                    var saleValue =
                        sale.SellingQuantity *
                        sale.SellingPrice;


                    return saleValue -
                           saleBuyValue;
                }
            );


        // =====================================================
        // UNREALIZED PROFIT
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
        //
        // Main holdings screen total remains current /
        // unrealized P/L.
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
                .Select(
                    x =>
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
                            consumedByPurchase
                                .TryGetValue(
                                    x.Id,
                                    out var sold
                                )
                                    ? sold
                                    : 0m;


                        // -----------------------------------------
                        // Remaining quantity
                        // -----------------------------------------

                        var remainingFromPurchase =
                            Math.Max(
                                0m,
                                x.BuyingQuantity -
                                soldFromPurchase
                            );


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
                        // Current value
                        // -----------------------------------------

                        decimal? purchaseCurrentValue =
                            currentPrice is null
                                ? null
                                : remainingFromPurchase *
                                  currentPrice.Value;


                        // -----------------------------------------
                        // Unrealized P/L
                        // -----------------------------------------

                        decimal? purchaseProfitLoss =
                            purchaseCurrentValue is null
                                ? null
                                : purchaseCurrentValue.Value -
                                  remainingBuyValueForPurchase;


                        // -----------------------------------------
                        // P/L %
                        // -----------------------------------------

                        decimal?
                            purchaseProfitLossPercentage =
                                remainingBuyValueForPurchase == 0
                                    ? remainingFromPurchase == 0
                                        ? 0m
                                        : null

                                    : purchaseProfitLoss is null
                                        ? null

                                        : purchaseProfitLoss.Value /
                                          remainingBuyValueForPurchase *
                                          100;


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
                            purchaseProfitLossPercentage
                        );
                    }
                )
                .ToArray();


        // =====================================================
        // SALE RESPONSE
        // =====================================================

        var sales =
            includedSales
                .OrderByDescending(x =>
                    x.SellingDate
                )
                .ThenByDescending(x =>
                    x.CreatedAtUtc
                )
                .ThenByDescending(x =>
                    x.Id
                )
                .Select(
                    sale =>
                    {
                        // =========================================
                        // FIFO ALLOCATIONS
                        // =========================================

                        IReadOnlyCollection<SaleAllocationResponse>
                            fifoAllocations;


                        // -----------------------------------------
                        // NEW SALE:
                        // use allocations stored in database
                        // -----------------------------------------

                        if (
                            sale.Allocations is not null &&
                            sale.Allocations.Count > 0)
                        {
                            fifoAllocations =
                                sale.Allocations

                                    .OrderBy(x =>
                                        x.BuyingDate
                                    )

                                    .ThenBy(x =>
                                        x.Id
                                    )

                                    .Select(
                                        allocation =>
                                        {
                                            var serialNo =
                                                purchasesById
                                                    .TryGetValue(
                                                        allocation.StockPurchaseId,
                                                        out var purchase
                                                    )
                                                        ? purchase.SerialNo
                                                        : 0;


                                            return new SaleAllocationResponse(
                                                allocation.StockPurchaseId,

                                                serialNo,

                                                allocation.BuyingDate,

                                                allocation.Quantity,

                                                allocation.BuyingPrice,

                                                allocation.BuyValue,

                                                allocation.HoldingDays,

                                                allocation.Term
                                            );
                                        }
                                    )
                                    .ToArray();
                        }

                        // -----------------------------------------
                        // OLD SALE:
                        // reconstruct FIFO from purchase history
                        // -----------------------------------------

                        else if (
                            replayAllocationsBySale
                                .TryGetValue(
                                    sale.Id,
                                    out var replayAllocations
                                ))
                        {
                            fifoAllocations =
                                replayAllocations;
                        }

                        else
                        {
                            fifoAllocations =
                                Array.Empty<
                                    SaleAllocationResponse
                                >();
                        }


                        // =========================================
                        // BUY VALUE
                        // =========================================

                        var fifoBuyValue =
                            fifoAllocations.Count > 0
                                ? fifoAllocations.Sum(
                                    x =>
                                        x.BuyValue
                                )
                                : sale.SellingQuantity *
                                  (
                                      sale.BuyPriceAtSale
                                      ??
                                      averageBuyingPrice
                                  );


                        // =========================================
                        // WEIGHTED FIFO BUY PRICE
                        // =========================================

                        var buyPrice =
                            sale.SellingQuantity == 0
                                ? 0m
                                : fifoBuyValue /
                                  sale.SellingQuantity;


                        // =========================================
                        // SALE VALUE
                        // =========================================

                        var saleValue =
                            sale.SellingQuantity *
                            sale.SellingPrice;


                        // =========================================
                        // REALIZED PROFIT / LOSS
                        // =========================================

                        var realizedProfitLoss =
                            saleValue -
                            fifoBuyValue;


                        // =========================================
                        // SALE RESPONSE
                        // =========================================

                        return new SaleResponse(
                            sale.Id,

                            sale.SellingDate,

                            sale.SellingQuantity,

                            sale.SellingPrice,

                            buyPrice,

                            fifoBuyValue,

                            saleValue,

                            realizedProfitLoss
                        )
                        {
                            FifoAllocations =
                                fifoAllocations
                        };
                    }
                )
                .ToArray();


        // =====================================================
        // BUYING DATE
        // =====================================================

        var buyingDate =
            purchases.Count == 0
                ? holding.BuyingDate
                : purchases.Min(
                    x =>
                        x.BuyingDate
                );


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

            // Main holding quantity = remaining quantity
            remainingQuantity,

            // Average buying price of remaining stocks
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

            // Remaining FIFO cost
            remainingBuyValue,

            currentValue,

            // Realized P/L
            realized,

            // Unrealized P/L
            unrealized,

            // Holdings total = current/unrealized P/L
            total,

            percentage,

            remainingQuantity == 0,

            sales
        )
        {
            // =================================================
            // PURCHASES
            // =================================================

            Purchases =
                purchaseResponses,


            // =================================================
            // AVERAGE SOLD RATE
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
                quote?.Source
                ??
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