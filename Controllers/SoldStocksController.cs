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
    private static readonly TimeSpan IndiaOffset = TimeSpan.FromHours(5.5);

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate)
    {
        if (fromDate.HasValue && toDate.HasValue && fromDate > toDate)
        {
            return BadRequest(new { message = "From date cannot be after to date." });
        }

        var userId = User.GetUserId();

        var query =
            from sale in db.StockSales.AsNoTracking()
            join holding in db.StockHoldings.AsNoTracking()
                on sale.StockHoldingId equals holding.Id
            where holding.UserId == userId
            select new
            {
                sale,
                holding
            };

        if (fromDate.HasValue)
        {
            query = query.Where(x => x.sale.SellingDate >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(x => x.sale.SellingDate <= toDate.Value);
        }

        var items = await query
            .OrderByDescending(x => x.sale.SellingDate)
            .ThenByDescending(x => x.sale.CreatedAtUtc)
            .Select(x => new
            {
                saleId = x.sale.Id,
                holdingId = x.sale.StockHoldingId,
                symbol = x.holding.Symbol,
                companyName = x.holding.CompanyName,
                exchange = x.holding.Exchange,
                buyingDate = x.holding.BuyingDate,
                buyingPrice = x.sale.BuyPriceAtSale ?? x.holding.BuyingPrice,
                sellingDate = x.sale.SellingDate,
                sellingQuantity = x.sale.SellingQuantity,
                sellingPrice = x.sale.SellingPrice,
                buyValue = x.sale.SellingQuantity * (x.sale.BuyPriceAtSale ?? x.holding.BuyingPrice),
                saleValue = x.sale.SellingQuantity * x.sale.SellingPrice,
                profitLoss =
                    (x.sale.SellingPrice - (x.sale.BuyPriceAtSale ?? x.holding.BuyingPrice)) *
                    x.sale.SellingQuantity,
                profitLossPercentage =
    (
        (
            (x.sale.SellingPrice -
                (x.sale.BuyPriceAtSale ?? x.holding.BuyingPrice))
            * x.sale.SellingQuantity
        )
        /
        (
            x.sale.SellingQuantity *
            (x.sale.BuyPriceAtSale ?? x.holding.BuyingPrice)
        )
    ) * 100,
                createdAtUtc = x.sale.CreatedAtUtc,
                createdAtIst = new DateTimeOffset(
                    DateTime.SpecifyKind(
                        x.sale.CreatedAtUtc,
                        DateTimeKind.Utc
                    )
                ).ToOffset(IndiaOffset)
            })
            .ToListAsync();

        return Ok(items);
    }
}