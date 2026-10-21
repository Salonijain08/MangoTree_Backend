using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Controllers;

[ApiController]
[Route("api/holdings/{holdingId:int}/sales")]
[Authorize]
public sealed class SalesController(AppDbContext db)
    : ControllerBase
{
    // =========================================================
    // CREATE SALE - FIFO
    // =========================================================

    [HttpPost]
    public async Task<IActionResult> Create(
        int holdingId,
        CreateSaleRequest request,
        CancellationToken ct)
    {
        var userIdValue =
            User.FindFirstValue(
                ClaimTypes.NameIdentifier
            );

        if (!int.TryParse(
                userIdValue,
                out var userId))
        {
            return Unauthorized(
                new
                {
                    message =
                        "Invalid user token."
                }
            );
        }


        // =====================================================
        // VALIDATION
        // =====================================================

        if (request.SellingQuantity <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling quantity must be greater than zero."
                }
            );
        }

        if (request.SellingPrice <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling price must be greater than zero."
                }
            );
        }


        // =====================================================
        // LOAD HOLDING + PURCHASES + SALES
        // =====================================================

        var holding =
            await db.StockHoldings
                .Include(x =>
                    x.Purchases)
                .Include(x =>
                    x.Sales)
                .FirstOrDefaultAsync(
                    x =>
                        x.Id ==
                            holdingId &&
                        x.UserId ==
                            userId,
                    ct
                );

        if (holding is null)
        {
            return NotFound(
                new
                {
                    message =
                        "The stock holding was not found."
                }
            );
        }


        // =====================================================
        // PURCHASE HISTORY
        // =====================================================

        var purchases =
            holding.Purchases
                .OrderBy(x =>
                    x.BuyingDate)
                .ThenBy(x =>
                    x.SerialNo)
                .ThenBy(x =>
                    x.Id)
                .ToList();

        if (purchases.Count == 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "No purchase history was found for this holding."
                }
            );
        }


        // =====================================================
        // SELLING DATE VALIDATION
        // =====================================================

        var earliestBuyingDate =
            purchases.Min(x =>
                x.BuyingDate);

        if (request.SellingDate <
            earliestBuyingDate)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling date cannot be before buying date."
                }
            );
        }


        // =====================================================
        // TOTAL PURCHASE / SALE QUANTITY
        // =====================================================

        var alreadySoldQuantity =
            holding.Sales.Sum(
                x =>
                    x.SellingQuantity
            );

        var totalPurchasedQuantity =
            purchases.Sum(
                x =>
                    x.BuyingQuantity
            );

        var totalRemainingQuantity =
            totalPurchasedQuantity -
            alreadySoldQuantity;


        if (totalRemainingQuantity <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "No quantity is available to sell."
                }
            );
        }


        if (request.SellingQuantity >
            totalRemainingQuantity)
        {
            return BadRequest(
                new
                {
                    message =
                        $"Only {totalRemainingQuantity} quantity is available to sell."
                }
            );
        }


        // =====================================================
        // REPLAY PREVIOUS SALES USING FIFO
        // =====================================================

        var previousSales =
            holding.Sales
                .OrderBy(x =>
                    x.SellingDate)
                .ThenBy(x =>
                    x.CreatedAtUtc)
                .ThenBy(x =>
                    x.Id)
                .ToList();


        var consumedByPurchase =
            purchases.ToDictionary(
                x => x.Id,
                _ => 0m
            );


        foreach (var previousSale
                 in previousSales)
        {
            var quantityToAllocate =
                previousSale
                    .SellingQuantity;


            foreach (var purchase
                     in purchases)
            {
                if (quantityToAllocate <= 0)
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


                if (availableFromPurchase <= 0)
                {
                    continue;
                }


                var consume =
                    Math.Min(
                        availableFromPurchase,
                        quantityToAllocate
                    );


                consumedByPurchase[
                    purchase.Id
                ] += consume;


                quantityToAllocate -=
                    consume;
            }


            if (quantityToAllocate > 0)
            {
                return BadRequest(
                    new
                    {
                        message =
                            "Existing sales exceed the available purchase quantity. " +
                            "Please verify the purchase and sale history."
                    }
                );
            }
        }


        // =====================================================
        // CREATE SALE
        // =====================================================

        var sale =
            new StockSale
            {
                StockHoldingId =
                    holding.Id,

                SellingDate =
                    request.SellingDate,

                SellingQuantity =
                    request.SellingQuantity,

                SellingPrice =
                    request.SellingPrice,

                CreatedAtUtc =
                    DateTime.UtcNow
            };


        // =====================================================
        // FIFO CURRENT SALE
        // =====================================================

        var saleQuantityRemaining =
            request.SellingQuantity;


        decimal totalBuyValueForSale =
            0m;


        var allocationResponses =
            new List<object>();


        foreach (var purchase
                 in purchases)
        {
            if (saleQuantityRemaining <= 0)
            {
                break;
            }


            var alreadyConsumed =
                consumedByPurchase[
                    purchase.Id
                ];


            var availableQuantity =
                Math.Max(
                    0m,
                    purchase.BuyingQuantity -
                    alreadyConsumed
                );


            if (availableQuantity <= 0)
            {
                continue;
            }


            var quantityFromThisPurchase =
                Math.Min(
                    availableQuantity,
                    saleQuantityRemaining
                );


            var buyValue =
                quantityFromThisPurchase *
                purchase.BuyingPrice;


            // =================================================
            // HOLDING PERIOD
            // =================================================

            var holdingDays =
                request.SellingDate.DayNumber -
                purchase.BuyingDate.DayNumber;


            var term =
                holdingDays > 365
                    ? "Long Term"
                    : "Short Term";


            // =================================================
            // SAVE FIFO ALLOCATION
            // =================================================

            var allocation =
                new StockSaleAllocation
                {
                    StockSale =
                        sale,

                    StockPurchaseId =
                        purchase.Id,

                    BuyingDate =
                        purchase.BuyingDate,

                    Quantity =
                        quantityFromThisPurchase,

                    BuyingPrice =
                        purchase.BuyingPrice,

                    BuyValue =
                        buyValue,

                    HoldingDays =
                        holdingDays,

                    Term =
                        term
                };


            sale.Allocations.Add(
                allocation
            );


            totalBuyValueForSale +=
                buyValue;


            saleQuantityRemaining -=
                quantityFromThisPurchase;


            // =================================================
            // API RESPONSE
            // =================================================

            allocationResponses.Add(
                new
                {
                    purchaseId =
                        purchase.Id,

                    serialNo =
                        purchase.SerialNo,

                    buyingDate =
                        purchase.BuyingDate,

                    buyingPrice =
                        purchase.BuyingPrice,

                    quantity =
                        quantityFromThisPurchase,

                    buyValue,

                    holdingDays,

                    term
                }
            );
        }


        if (saleQuantityRemaining > 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Unable to allocate the complete selling quantity using FIFO."
                }
            );
        }


        // =====================================================
        // FIFO WEIGHTED BUY PRICE
        // =====================================================

        var fifoAverageBuyPrice =
            request.SellingQuantity == 0
                ? 0m
                : totalBuyValueForSale /
                  request.SellingQuantity;


        sale.BuyPriceAtSale =
            fifoAverageBuyPrice;


        // =====================================================
        // SAVE SALE + ALLOCATIONS
        // =====================================================

        db.StockSales.Add(
            sale
        );


        holding.UpdatedAtUtc =
            DateTime.UtcNow;


        await db.SaveChangesAsync(
            ct
        );


        // =====================================================
        // CALCULATIONS
        // =====================================================

        var saleValue =
            sale.SellingQuantity *
            sale.SellingPrice;


        var profitLoss =
            saleValue -
            totalBuyValueForSale;


        // =====================================================
        // RESPONSE
        // =====================================================

        return Ok(
            new
            {
                message =
                    "Stock sale was recorded successfully.",

                saleId =
                    sale.Id,

                holdingId =
                    holding.Id,

                sellingDate =
                    sale.SellingDate,

                sellingQuantity =
                    sale.SellingQuantity,

                sellingPrice =
                    sale.SellingPrice,

                buyPriceAtSale =
                    sale.BuyPriceAtSale,

                buyValue =
                    totalBuyValueForSale,

                saleValue,

                profitLoss,

                fifoAllocations =
                    allocationResponses,

                createdAtUtc =
                    sale.CreatedAtUtc
            }
        );
    }
}


// =============================================================
// REQUEST
// =============================================================

public sealed class CreateSaleRequest
{
    public DateOnly SellingDate
    {
        get;
        set;
    }

    public decimal SellingQuantity
    {
        get;
        set;
    }

    public decimal SellingPrice
    {
        get;
        set;
    }
}