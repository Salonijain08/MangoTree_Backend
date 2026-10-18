using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Helpers;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Services;

public sealed class HoldingsPriceService(AppDbContext db, IMarketDataService yahoo,
    NseBhavcopyService nse, TimeProvider clock)
{
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<HoldingsPriceResponse> GetAsync(int userId, DateOnly? requestedDate,
        bool syncClosing, int? holdingId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().ToOffset(MarketClock.IndiaOffset);
        var today = DateOnly.FromDateTime(now.DateTime);
        var selected = requestedDate ?? today;
        var historical = selected < today;
        if (selected > today)
            throw new MarketDataException("FUTURE_DATE", "Select today or a previous date (IST).");
        if (historical && selected < MarketClock.FirstUdIfDate)
            throw new MarketDataException("UNSUPPORTED_DATE", "Historical dates in this version start at 2024-07-08 (NSE UDiFF).");
        var canSync = now.TimeOfDay >= TimeSpan.FromHours(17);
        if (syncClosing && !historical && !canSync)
            throw new MarketDataException("BEFORE_CLOSING_SYNC", "Today's NSE closing sync is available after 5:00 PM IST.");

        var gate = Locks[(int)((uint)userId % (uint)Locks.Length)];
        await gate.WaitAsync(ct);
        try
        {
            var query = db.StockHoldings.Include(x => x.Sales).Include(x => x.Purchases)
                .Where(x => x.UserId == userId && x.BuyingDate <= selected);
            if (holdingId is { } id) query = query.Where(x => x.Id == id);
            var holdings = await query
      .OrderByDescending(x => x.BuyingDate)
      .ThenBy(x => x.Symbol)
      .ToListAsync(ct);

            IReadOnlyDictionary<string, StockClosingPrice>? daily = null;
            MarketDataException? dailyError = null;
            if (holdings.Any(x => x.Exchange.Trim().Equals("NSE", StringComparison.OrdinalIgnoreCase)) &&
                (historical || syncClosing))
            {
                try { daily = await nse.GetDateAsync(selected, ct); }
                catch (MarketDataException ex) { dailyError = ex; }
            }

            var errors = new List<PriceIssue>();
            var items = new List<HoldingResponse>();
            var synced = 0;
            foreach (var holding in holdings)
            {
                ct.ThrowIfCancellationRequested();
                PriceQuote? quote = null;
                var status = "UNAVAILABLE";
                string? message = null;
                try
                {
                    var symbol = TradingSymbols.Normalize(holding.Symbol, holding.Exchange);
                    if (historical || syncClosing)
                    {
                        MarketDataException? error = null;
                        if (!holding.Exchange.Trim().Equals("NSE", StringComparison.OrdinalIgnoreCase))
                            error = new("UNSUPPORTED_EXCHANGE", "Official closing prices in this version support NSE EQ shares only.");
                        else if (dailyError is not null) error = dailyError;
                        else if (daily is null || !daily.TryGetValue(symbol, out var row))
                            error = new("NSE_SYMBOL_NOT_FOUND", $"No NSE EQ closing price for {symbol} on {selected:yyyy-MM-dd}.");
                        else
                        {
                            quote = new PriceQuote(row.ClosingPrice, row.PriceDate, "NSE_BHAVCOPY", null, row.FetchedAtUtc);
                            status = "FINAL";
                            synced++;
                            if (!historical)
                            {
                                holding.LatestClosingPrice = row.ClosingPrice;
                                holding.ClosingPriceDate = row.PriceDate;
                                holding.ClosingPriceSource = "NSE_BHAVCOPY";
                                holding.ClosingPriceFetchedAtUtc = row.FetchedAtUtc;
                                holding.UpdatedAtUtc = now.UtcDateTime;
                            }
                        }
                        if (error is not null)
                        {
                            message = error.Message;
                            errors.Add(new PriceIssue(holding.Id, holding.Symbol, error.Code, error.Message));
                        }
                    }

                    if (!historical && quote is null)
                    {
                        // A manual close sync wins over subsequent Yahoo polls for this trading date.
                        var saved = HoldingMapper.SavedQuote(holding, today);
                        if (saved?.Source == "NSE_BHAVCOPY")
                        {
                            quote = saved;
                            status = "FINAL";
                        }
                        else
                        {
                            try
                            {
                                quote = await yahoo.GetLatestTradeAsync(holding, ct);
                                holding.LatestTradePrice = quote.Price;
                                holding.LatestTradePriceTimeUtc = quote.QuoteTimeUtc;
                                holding.LatestTradePriceFetchedAtUtc = quote.FetchedAtUtc;
                                status = quote.Date < today ? "LAST_TRADE" : "LATEST_AVAILABLE";
                            }
                            catch (MarketDataException ex)
                            {
                                errors.Add(new PriceIssue(holding.Id, holding.Symbol, ex.Code, ex.Message));
                                message = message is null ? ex.Message : message + " " + ex.Message;
                                quote = saved;
                                status = quote is null ? "UNAVAILABLE" : "STALE";
                            }
                        }
                    }
                }
                catch (MarketDataException ex)
                {
                    errors.Add(new PriceIssue(holding.Id, holding.Symbol, ex.Code, ex.Message));
                    message = ex.Message;
                }
                items.Add(holding.ToResponse(selected, historical, quote, status, message));
            }
            if (!historical) await db.SaveChangesAsync(ct);
            var failed = errors.Select(x => x.Id).Distinct().Count();
            var mode = historical ? "HISTORICAL" : "TODAY";
            var summary = holdings.Count == 0 ? "No holdings for the selected date."
                : historical ? (failed == 0 ? "NSE closing prices loaded for the selected date."
                    : "Some selected-date NSE closing prices are unavailable. No alternate date was used.")
                : syncClosing ? $"{synced} NSE closing prices synced. Other holdings retain their latest available price."
                : "Latest available prices loaded. Today's manually synced NSE close takes priority.";
            return new HoldingsPriceResponse(today, selected, now, canSync, mode, summary,
                synced, failed, errors, items);
        }
        finally { gate.Release(); }
    }
}
