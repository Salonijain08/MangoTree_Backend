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
public sealed class SalesController(AppDbContext db) : ControllerBase
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
            User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (!int.TryParse(userIdValue, out var userId))
        {
            return Unauthorized(
                new
                {
                    message = "Invalid user token."
                });
        }

        // -----------------------------------------------------
        // Validation
        // -----------------------------------------------------

        if (request.SellingQuantity <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling quantity must be greater than zero."
                });
        }

        if (request.SellingPrice <= 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling price must be greater than zero."
                });
        }

        // -----------------------------------------------------
        // Load holding + purchases + sales
        // -----------------------------------------------------

        var holding = await db.StockHoldings
            .Include(x => x.Purchases)
            .Include(x => x.Sales)
            .FirstOrDefaultAsync(
                x =>
                    x.Id == holdingId &&
                    x.UserId == userId,
                ct);

        if (holding is null)
        {
            return NotFound(
                new
                {
                    message =
                        "The stock holding was not found."
                });
        }

        if (request.SellingDate < holding.BuyingDate)
        {
            return BadRequest(
                new
                {
                    message =
                        "Selling date cannot be before buying date."
                });
        }

        // -----------------------------------------------------
        // Get purchases in FIFO order
        //
        // Oldest purchase first.
        // If same date -> Serial No.
        // -----------------------------------------------------

        var purchases = holding.Purchases
            .OrderBy(x => x.BuyingDate)
            .ThenBy(x => x.SerialNo)
            .ThenBy(x => x.Id)
            .ToList();

        if (purchases.Count == 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "No purchase history was found for this holding."
                });
        }

        // -----------------------------------------------------
        // Calculate already consumed quantity from previous sales
        //
        // Previous sales are also considered FIFO.
        // -----------------------------------------------------

        var alreadySoldQuantity =
            holding.Sales.Sum(x => x.SellingQuantity);

        var totalPurchasedQuantity =
            purchases.Sum(x => x.BuyingQuantity);

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
                });
        }

        if (request.SellingQuantity >
            totalRemainingQuantity)
        {
            return BadRequest(
                new
                {
                    message =
                        $"Only {totalRemainingQuantity} quantity is available to sell."
                });
        }

        // =====================================================
        // IMPORTANT:
        //
        // We need to know how much of each purchase lot has
        // already been consumed by previous sales.
        //
        // We replay previous sales in chronological order
        // using FIFO.
        // =====================================================

        var previousSales = holding.Sales
            .OrderBy(x => x.SellingDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ThenBy(x => x.Id)
            .ToList();

        var consumedByPurchase =
            purchases.ToDictionary(
                x => x.Id,
                _ => 0m);

        foreach (var previousSale in previousSales)
        {
            var quantityToAllocate =
                previousSale.SellingQuantity;

            foreach (var purchase in purchases)
            {
                if (quantityToAllocate <= 0)
                    break;

                var alreadyConsumed =
                    consumedByPurchase[purchase.Id];

                var availableFromPurchase =
                    Math.Max(
                        0m,
                        purchase.BuyingQuantity -
                        alreadyConsumed);

                if (availableFromPurchase <= 0)
                    continue;

                var consume =
                    Math.Min(
                        availableFromPurchase,
                        quantityToAllocate);

                consumedByPurchase[purchase.Id] +=
                    consume;

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
                    });
            }
        }

        // =====================================================
        // ALLOCATE CURRENT SALE USING FIFO
        // =====================================================

        var saleQuantityRemaining =
            request.SellingQuantity;

        decimal totalBuyValueForSale = 0m;

        var allocations =
            new List<object>();

        foreach (var purchase in purchases)
        {
            if (saleQuantityRemaining <= 0)
                break;

            var alreadyConsumed =
                consumedByPurchase[purchase.Id];

            var availableQuantity =
                Math.Max(
                    0m,
                    purchase.BuyingQuantity -
                    alreadyConsumed);

            if (availableQuantity <= 0)
                continue;

            var quantityFromThisPurchase =
                Math.Min(
                    availableQuantity,
                    saleQuantityRemaining);

            var buyValue =
                quantityFromThisPurchase *
                purchase.BuyingPrice;

            totalBuyValueForSale +=
                buyValue;

            saleQuantityRemaining -=
                quantityFromThisPurchase;

            allocations.Add(
                new
                {
                    purchaseId = purchase.Id,
                    serialNo = purchase.SerialNo,
                    buyingDate = purchase.BuyingDate,
                    buyingPrice = purchase.BuyingPrice,
                    quantity = quantityFromThisPurchase,
                    buyValue
                });
        }

        if (saleQuantityRemaining > 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "Unable to allocate the complete selling quantity using FIFO."
                });
        }

        // =====================================================
        // WEIGHTED FIFO COST FOR THIS SALE
        // =====================================================

        var fifoAverageBuyPrice =
            request.SellingQuantity == 0
                ? 0m
                : totalBuyValueForSale /
                  request.SellingQuantity;

        // =====================================================
        // CREATE SALE
        //
        // BuyPriceAtSale stores the frozen weighted FIFO cost.
        //
        // Example:
        //
        // 5 @ 1500
        // 2 @ 1300
        //
        // Sale 7
        //
        // Buy value = 10100
        // Average cost = 1442.857...
        //
        // =====================================================

        var sale = new StockSale
        {
            StockHoldingId =
                holding.Id,

            SellingDate =
                request.SellingDate,

            SellingQuantity =
                request.SellingQuantity,

            SellingPrice =
                request.SellingPrice,

            BuyPriceAtSale =
                fifoAverageBuyPrice,

            CreatedAtUtc =
                DateTime.UtcNow
        };

        db.StockSales.Add(sale);

        holding.UpdatedAtUtc =
            DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // =====================================================
        // RESPONSE
        // =====================================================

        var saleValue =
            sale.SellingQuantity *
            sale.SellingPrice;

        var profitLoss =
            saleValue -
            totalBuyValueForSale;

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
                    allocations,

                createdAtUtc =
                    sale.CreatedAtUtc
            });
    }
}


// =============================================================
// REQUEST
// =============================================================

public sealed class CreateSaleRequest
{
    public DateOnly SellingDate { get; set; }

    public decimal SellingQuantity { get; set; }

    public decimal SellingPrice { get; set; }
}