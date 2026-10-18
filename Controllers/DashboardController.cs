using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Helpers;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(AppDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(fromDate, toDate, ct);
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(fromDate, toDate, ct);
        return Ok(result.Summary);
    }

    [HttpGet("recent-sales")]
    public async Task<IActionResult> GetRecentSales(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(fromDate, toDate, ct);
        return Ok(result.RecentSales);
    }

    [HttpGet("top-holdings")]
    public async Task<IActionResult> GetTopHoldings(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(fromDate, toDate, ct);

        return Ok(new
        {
            profit = result.TopProfitHoldings,
            loss = result.TopLossHoldings
        });
    }

    private async Task<DashboardResponse> BuildDashboard(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(fromDate, toDate);
        var userId = User.GetUserId();

        var holdings = await db.StockHoldings
            .Include(x => x.Purchases)
            .Include(x => x.Sales)
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        var activeRows = holdings
            .Select(holding =>
            {
                var purchases = holding.Purchases
                    .Where(x => x.BuyingDate <= to)
                    .ToList();

                var quantity = purchases.Sum(x => x.BuyingQuantity);
                var investment = purchases.Sum(x => x.BuyingQuantity * x.BuyingPrice);
                var averagePrice = quantity == 0 ? 0m : investment / quantity;

                var soldQuantity = holding.Sales
                    .Where(x => x.SellingDate <= to)
                    .Sum(x => x.SellingQuantity);

                var remainingQuantity = Math.Max(0m, quantity - soldQuantity);
                var price = GetDisplayPrice(holding);

                var currentValue = price.HasValue
                    ? remainingQuantity * price.Value
                    : 0m;

                var profitLoss = price.HasValue
                    ? (price.Value - averagePrice) * remainingQuantity
                    : 0m;

                var percentage = investment == 0
                    ? 0m
                    : profitLoss / investment * 100;

                return new
                {
                    Holding = holding,
                    Quantity = remainingQuantity,
                    AveragePrice = averagePrice,
                    Investment = investment,
                    Price = price,
                    CurrentValue = currentValue,
                    ProfitLoss = profitLoss,
                    Percentage = percentage
                };
            })
            .Where(x => x.Quantity > 0)
            .ToList();

        var sales = holdings
            .SelectMany(holding =>
                holding.Sales
                    .Where(sale =>
                        sale.SellingDate >= from &&
                        sale.SellingDate <= to)
                    .Select(sale => new
                    {
                        Holding = holding,
                        Sale = sale
                    }))
            .ToList();

        var holdingRows = activeRows
            .Select(x => new DashboardHoldingResponse(
                x.Holding.Id,
                x.Holding.Symbol,
                x.Holding.CompanyName,
                x.Holding.Exchange,
                x.Quantity,
                x.AveragePrice,
                x.Price,
                x.Investment,
                x.CurrentValue,
                x.ProfitLoss,
                x.Percentage))
            .ToList();

        var soldRows = sales
            .Select(x =>
            {
                var buyPrice = x.Sale.BuyPriceAtSale ?? x.Holding.BuyingPrice;
                var buyValue = x.Sale.SellingQuantity * buyPrice;
                var saleValue = x.Sale.SellingQuantity * x.Sale.SellingPrice;
                var profitLoss = saleValue - buyValue;

                return new DashboardSoldStockResponse(
                    x.Sale.Id,
                    x.Holding.Id,
                    x.Holding.Symbol,
                    x.Holding.CompanyName,
                    x.Holding.Exchange,
                    x.Holding.BuyingDate,
                    buyPrice,
                    x.Sale.SellingDate,
                    x.Sale.SellingQuantity,
                    x.Sale.SellingPrice,
                    buyValue,
                    saleValue,
                    profitLoss,
                    x.Sale.CreatedAtUtc);
            })
            .ToList();

        var totalInvestment = holdingRows.Sum(x => x.Investment);
        var totalCurrentValue = holdingRows.Sum(x => x.CurrentValue);
        var totalUnrealized = holdingRows.Sum(x => x.ProfitLoss);
        var totalRealized = soldRows.Sum(x => x.ProfitLoss);
        var totalProfitLoss = totalUnrealized + totalRealized;

        var percentage = totalInvestment == 0
            ? 0m
            : totalProfitLoss / totalInvestment * 100;

        var summary = new DashboardSummaryResponse(
            from,
            to,
            holdingRows.Count,
            soldRows.Count,
            totalInvestment,
            totalCurrentValue,
            totalUnrealized,
            totalRealized,
            totalProfitLoss,
            percentage,
            holdingRows.Sum(x => x.Quantity),
            soldRows.Sum(x => x.SellingQuantity),
            soldRows.Sum(x => x.BuyValue),
            soldRows.Sum(x => x.SaleValue),
            soldRows.Count(x => x.ProfitLoss > 0),
            soldRows.Count(x => x.ProfitLoss < 0));

        return new DashboardResponse(
            summary,
            holdingRows
                .Where(x => x.ProfitLoss > 0)
                .OrderByDescending(x => x.ProfitLoss)
                .Take(5)
                .ToArray(),
            holdingRows
                .Where(x => x.ProfitLoss < 0)
                .OrderBy(x => x.ProfitLoss)
                .Take(5)
                .ToArray(),
            soldRows
                .OrderByDescending(x => x.CreatedAtUtc)
                .Take(10)
                .ToArray());
    }

    private static decimal? GetDisplayPrice(StockHolding holding)
    {
        if (holding.ClosingPriceSource == "NSE_BHAVCOPY" &&
            holding.LatestClosingPrice.HasValue)
            return holding.LatestClosingPrice.Value;

        if (holding.LatestTradePrice.HasValue)
            return holding.LatestTradePrice.Value;

        return holding.LatestClosingPrice;
    }

    private static (DateOnly From, DateOnly To) ResolveDateRange(
        DateOnly? fromDate,
        DateOnly? toDate)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddMinutes(330));

        var from = fromDate ??
                   new DateOnly(today.Year, today.Month, 1);

        var to = toDate ?? today;

        if (from > to)
            throw new InvalidOperationException("From date cannot be greater than to date.");

        return (from, to);
    }
}
