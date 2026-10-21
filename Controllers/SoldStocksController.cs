using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.Helpers;

namespace StockPortfolio.Api.Controllers;

[ApiController]
[Route("api/sold-stocks")]
[Authorize]
public sealed class SoldStocksController(AppDbContext db) : ControllerBase
{
    private static readonly TimeSpan IndiaOffset =
        TimeSpan.FromHours(5.5);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate)
    {
        if (
            fromDate.HasValue &&
            toDate.HasValue &&
            fromDate > toDate)
        {
            return BadRequest(
                new
                {
                    message =
                        "From date cannot be after to date."
                }
            );
        }

        var userId =
            User.GetUserId();


        // =========================================================
        // LOAD SALES + HOLDING + FIFO ALLOCATIONS + PURCHASES
        // =========================================================

        var query =
            db.StockSales
                .AsNoTracking()

                .Include(x =>
                    x.Allocations)

                .ThenInclude(x =>
                    x.StockPurchase)

                .Include(x =>
                    x.StockHolding)

                .Where(x =>
                    x.StockHolding.UserId ==
                    userId);


        // =========================================================
        // DATE FILTER
        // =========================================================

        if (fromDate.HasValue)
        {
            query =
                query.Where(x =>
                    x.SellingDate >=
                    fromDate.Value);
        }


        if (toDate.HasValue)
        {
            query =
                query.Where(x =>
                    x.SellingDate <=
                    toDate.Value);
        }


        // =========================================================
        // LOAD DATA
        // =========================================================

        var sales =
            await query
                .OrderByDescending(x =>
                    x.SellingDate)

                .ThenByDescending(x =>
                    x.CreatedAtUtc)

                .ThenByDescending(x =>
                    x.Id)

                .ToListAsync();


        // =========================================================
        // RESPONSE
        // =========================================================

        var items =
            sales.Select(
                sale =>
                {
                    var holding =
                        sale.StockHolding;


                    // =================================================
                    // FIFO ALLOCATIONS
                    // =================================================

                    var fifoAllocations =
                        sale.Allocations

                            .OrderBy(x =>
                                x.BuyingDate)

                            .ThenBy(x =>
                                x.Id)

                            .Select(
                                allocation =>
                                {
                                    var purchase =
                                        allocation.StockPurchase;


                                    return new
                                    {
                                        purchaseId =
                                            allocation.StockPurchaseId,

                                        serialNo =
                                            purchase?.SerialNo ?? 0,

                                        buyingDate =
                                            allocation.BuyingDate,

                                        quantity =
                                            allocation.Quantity,

                                        buyingPrice =
                                            allocation.BuyingPrice,

                                        buyValue =
                                            allocation.BuyValue,

                                        holdingDays =
                                            allocation.HoldingDays,

                                        term =
                                            allocation.Term,

                                        saleValue =
                                            allocation.Quantity *
                                            sale.SellingPrice,

                                        profitLoss =
                                            (
                                                sale.SellingPrice -
                                                allocation.BuyingPrice
                                            )
                                            *
                                            allocation.Quantity
                                    };
                                }
                            )
                            .ToArray();


                    // =================================================
                    // TOTAL BUY VALUE
                    // =================================================

                    var buyValue =
                        fifoAllocations.Length > 0

                            ? fifoAllocations.Sum(
                                x =>
                                    x.buyValue
                            )

                            : sale.SellingQuantity *
                              (
                                  sale.BuyPriceAtSale
                                  ??
                                  holding.BuyingPrice
                              );


                    // =================================================
                    // WEIGHTED BUY PRICE
                    // =================================================

                    var buyingPrice =
                        sale.SellingQuantity == 0

                            ? 0m

                            : buyValue /
                              sale.SellingQuantity;


                    // =================================================
                    // SALE VALUE
                    // =================================================

                    var saleValue =
                        sale.SellingQuantity *
                        sale.SellingPrice;


                    // =================================================
                    // PROFIT / LOSS
                    // =================================================

                    var profitLoss =
                        saleValue -
                        buyValue;


                    var profitLossPercentage =
                        buyValue == 0

                            ? 0m

                            : profitLoss /
                              buyValue *
                              100;


                    // =================================================
                    // MAIN BUYING DATE
                    //
                    // For display only:
                    // oldest FIFO purchase date used in this sale.
                    // Exact dates remain in fifoAllocations.
                    // =================================================

                    var buyingDate =
                        fifoAllocations.Length > 0

                            ? fifoAllocations
                                .Min(x =>
                                    x.buyingDate)

                            : holding.BuyingDate;


                    // =================================================
                    // SALE-LEVEL TERM
                    //
                    // MIXED = some FIFO lots short-term and some long-term
                    // =================================================

                    string term;


                    if (fifoAllocations.Length == 0)
                    {
                        term =
                            "Unknown";
                    }
                    else
                    {
                        var distinctTerms =
                            fifoAllocations
                                .Select(x =>
                                    x.term)

                                .Distinct(
                                    StringComparer.OrdinalIgnoreCase)

                                .ToArray();


                        term =
                            distinctTerms.Length == 1
                                ? distinctTerms[0]
                                : "Mixed";
                    }


                    return new
                    {
                        saleId =
                            sale.Id,

                        holdingId =
                            sale.StockHoldingId,

                        symbol =
                            holding.Symbol,

                        companyName =
                            holding.CompanyName,

                        exchange =
                            holding.Exchange,

                        buyingDate,

                        buyingPrice,

                        sellingDate =
                            sale.SellingDate,

                        sellingQuantity =
                            sale.SellingQuantity,

                        sellingPrice =
                            sale.SellingPrice,

                        buyValue,

                        saleValue,

                        profitLoss,

                        profitLossPercentage,

                        term,

                        fifoAllocations,

                        createdAtUtc =
                            sale.CreatedAtUtc,

                        createdAtIst =
                            new DateTimeOffset(
                                DateTime.SpecifyKind(
                                    sale.CreatedAtUtc,
                                    DateTimeKind.Utc
                                )
                            )
                            .ToOffset(
                                IndiaOffset
                            )
                    };
                }
            )
            .ToArray();


        return Ok(items);
    }
}