
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Helpers;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Services;

namespace StockPortfolio.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public sealed class DashboardController(AppDbContext db) : ControllerBase
{
    // ==========================================
    // GET COMPLETE DASHBOARD
    // ==========================================

    [HttpGet]
    public async Task<IActionResult> GetDashboard(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(
            fromDate,
            toDate,
            ct);

        return Ok(result);
    }

    // ==========================================
    // GET DASHBOARD SUMMARY
    // ==========================================

    [HttpGet("summary")]
    public async Task<IActionResult> GetSummary(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(
            fromDate,
            toDate,
            ct);

        return Ok(result.Summary);
    }

    // ==========================================
    // GET RECENT SALES
    // ==========================================

    [HttpGet("recent-sales")]
    public async Task<IActionResult> GetRecentSales(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(
            fromDate,
            toDate,
            ct);

        return Ok(result.RecentSales);
    }

    // ==========================================
    // GET TOP PROFIT AND LOSS HOLDINGS
    // ==========================================

    [HttpGet("top-holdings")]
    public async Task<IActionResult> GetTopHoldings(
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await BuildDashboard(
            fromDate,
            toDate,
            ct);

        return Ok(new
        {
            profit = result.TopProfitHoldings,
            loss = result.TopLossHoldings
        });
    }

    // ==========================================
    // BUILD DASHBOARD
    // ==========================================

    private async Task<DashboardResponse> BuildDashboard(
        DateOnly? fromDate,
        DateOnly? toDate,
        CancellationToken ct)
    {
        var (from, to) = ResolveDateRange(
            fromDate,
            toDate);

        var userId = User.GetUserId();

        // ==========================================
        // LOAD HOLDINGS WITH PURCHASES AND SALES
        // ==========================================

        var holdings = await db.StockHoldings
            .AsNoTracking()
            .AsSplitQuery()
            .Include(x => x.Purchases)
            .Include(x => x.Sales)
                .ThenInclude(x => x.Allocations)
            .Where(x => x.UserId == userId)
            .ToListAsync(ct);

        var today = MarketClock.Today;

        var isHistorical = to != today;

        // ==========================================
        // HOLDING CALCULATION USING HOLDING MAPPER
        // ==========================================

        var holdingData = holdings
            .Select(holding =>
            {
                // Use the same price-selection logic
                // as the main holdings screen.
                //
                // Historical dates require historical
                // price records, not today's cached quote.

                PriceQuote? quote = null;

                if (!isHistorical)
                {
                    quote = HoldingMapper.SavedQuote(
                        holding,
                        today);
                }

                var status = isHistorical
                    ? "UNAVAILABLE"
                    : quote?.Source == "NSE_BHAVCOPY"
                        ? "FINAL"
                        : quote is null
                            ? "UNAVAILABLE"
                            : "CACHED";

                var response = holding.ToResponse(
                    to,
                    isHistorical,
                    quote,
                    status,
                    isHistorical
                        ? "Historical price lookup is not available in this dashboard."
                        : null);

                return new
                {
                    Holding = holding,
                    Response = response,
                    Price = quote?.Price
                };
            })
            .ToList();

        // ==========================================
        // ACTIVE HOLDINGS
        // ==========================================

        var holdingRows = holdingData
            .Where(x =>
                x.Response.RemainingQuantity > 0)
            .Select(x =>
            {
                var response = x.Response;

                // FIFO remaining investment
                var investment = response.RemainingCost;

                // Same valuation as holdings API
                var currentValue =
                    response.CurrentValue ?? 0m;

                var profitLoss =
                    response.UnrealizedProfitLoss ?? 0m;

                var percentage =
                    response.ProfitLossPercentage ?? 0m;

                return new DashboardHoldingResponse(
                    x.Holding.Id,
                    x.Holding.Symbol,
                    x.Holding.CompanyName,
                    x.Holding.Exchange,
                    response.RemainingQuantity,
                    response.AverageBuyingPrice,
                    x.Price,
                    investment,
                    currentValue,
                    profitLoss,
                    percentage);
            })
            .ToList();

        // ==========================================
        // SALES WITHIN SELECTED DATE RANGE
        // ==========================================

        var soldRows = holdingData
            .SelectMany(x =>
            {
                var mappedSales = x.Response.Sales
                    .ToDictionary(s => s.Id);

                return x.Holding.Sales
                    .Where(sale =>
                        sale.SellingDate >= from &&
                        sale.SellingDate <= to)
                    .Select(sale =>
                    {
                        mappedSales.TryGetValue(
                            sale.Id,
                            out var mappedSale);

                        // Prefer FIFO-calculated values
                        // from HoldingMapper.
                        var buyPrice = mappedSale?.BuyPrice
                            ?? sale.BuyPriceAtSale
                            ?? x.Holding.BuyingPrice;

                        var buyValue = mappedSale?.BuyValue
                            ?? sale.SellingQuantity * buyPrice;

                        var saleValue = mappedSale?.SaleValue
                            ?? sale.SellingQuantity *
                               sale.SellingPrice;

                        var profitLoss =
                            saleValue - buyValue;

                        return new DashboardSoldStockResponse(
                            sale.Id,
                            x.Holding.Id,
                            x.Holding.Symbol,
                            x.Holding.CompanyName,
                            x.Holding.Exchange,
                            x.Holding.BuyingDate,
                            buyPrice,
                            sale.SellingDate,
                            sale.SellingQuantity,
                            sale.SellingPrice,
                            buyValue,
                            saleValue,
                            profitLoss,
                            sale.CreatedAtUtc);
                    });
            })
            .ToList();

        // ==========================================
        // TOTAL INVESTMENT
        // ==========================================

        var totalInvestment = holdingRows.Sum(
            x => x.Investment);

        // ==========================================
        // TOTAL CURRENT VALUE
        // ==========================================

        var totalCurrentValue = holdingRows.Sum(
            x => x.CurrentValue);

        // ==========================================
        // TOTAL UNREALIZED PROFIT / LOSS
        // ==========================================

        var totalUnrealized = holdingRows.Sum(
            x => x.ProfitLoss);

        // ==========================================
        // TOTAL REALIZED PROFIT / LOSS
        // ==========================================

        var totalRealized = soldRows.Sum(
            x => x.ProfitLoss);

        // ==========================================
        // TOTAL PROFIT / LOSS
        // ==========================================

        var totalProfitLoss =
            totalUnrealized + totalRealized;

        // ==========================================
        // TOTAL PROFIT PERCENTAGE
        // ==========================================

        var percentage = totalInvestment > 0
            ? totalProfitLoss / totalInvestment * 100m
            : 0m;

        // ==========================================
        // DASHBOARD SUMMARY RESPONSE
        // ==========================================

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

        // ==========================================
        // TOP 5 PROFIT HOLDINGS
        // ==========================================

        var topProfitHoldings = holdingRows
            .Where(x => x.ProfitLoss > 0)
            .OrderByDescending(x => x.ProfitLoss)
            .Take(5)
            .ToArray();

        // ==========================================
        // TOP 5 LOSS HOLDINGS
        // ==========================================

        var topLossHoldings = holdingRows
            .Where(x => x.ProfitLoss < 0)
            .OrderBy(x => x.ProfitLoss)
            .Take(5)
            .ToArray();

        // ==========================================
        // RECENT 10 SALES
        // ==========================================

        var recentSales = soldRows
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(10)
            .ToArray();

        // ==========================================
        // FINAL DASHBOARD RESPONSE
        // ==========================================

        return new DashboardResponse(
            summary,
            topProfitHoldings,
            topLossHoldings,
            recentSales);
    }

    // ==========================================
    // RESOLVE DATE RANGE
    // ==========================================

    private static (DateOnly From, DateOnly To)
        ResolveDateRange(
            DateOnly? fromDate,
            DateOnly? toDate)
    {
        var today = MarketClock.Today;

        var from = fromDate ??
            new DateOnly(
                today.Year,
                today.Month,
                1);

        var to = toDate ?? today;

        if (from > to)
        {
            throw new InvalidOperationException(
                "From date cannot be greater than to date.");
        }

        if (to > today)
        {
            throw new InvalidOperationException(
                "To date cannot be in the future.");
        }

        return (from, to);
    }
}
