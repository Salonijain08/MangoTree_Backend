using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Services;

// Interface and implementation remain together in this file.
public interface IMarketDataService
{
    Task<PriceQuote> GetLatestTradeAsync(StockHolding holding, CancellationToken ct);
}

public sealed class YahooFinanceMarketDataService(HttpClient httpClient, IMemoryCache cache)
    : IMarketDataService
{
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<PriceQuote> GetLatestTradeAsync(StockHolding holding, CancellationToken ct)
    {
        var symbol = TradingSymbols.Normalize(holding.Symbol, holding.Exchange);
        var ticker = symbol + (holding.Exchange.Trim().Equals("BSE", StringComparison.OrdinalIgnoreCase) ? ".BO" : ".NS");
        var key = "yahoo-ltp:" + ticker;
        var gate = Locks[(int)((uint)StringComparer.Ordinal.GetHashCode(key) % (uint)Locks.Length)];
        await gate.WaitAsync(ct);
        try
        {
            if (cache.TryGetValue<PriceQuote>(key, out var saved) && saved is not null) return saved;
            if (cache.TryGetValue<MarketDataException>(key + ":error", out var recentError) && recentError is not null)
                throw recentError;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(20));
                var url = $"/v8/finance/chart/{Uri.EscapeDataString(ticker)}?range=1d&interval=1m";
                using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (!response.IsSuccessStatusCode)
                    throw new MarketDataException("YAHOO_HTTP_ERROR", $"Yahoo request for {ticker} returned HTTP {(int)response.StatusCode}.");
                var bytes = await MarketHttp.ReadLimitedAsync(response.Content, 4_000_000, timeout.Token);
                var quote = ParseQuote(bytes, ticker, DateTimeOffset.UtcNow);
                cache.Set(key, quote, TimeSpan.FromSeconds(60));
                return quote;
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var error = new MarketDataException("YAHOO_TIMEOUT", "Yahoo request timed out. Previously fetched price is retained, if available.");
                cache.Set(key + ":error", error, TimeSpan.FromSeconds(30));
                throw error;
            }
            catch (HttpRequestException)
            {
                var error = new MarketDataException("YAHOO_UNAVAILABLE", "Yahoo could not be reached. Previously fetched price is retained, if available.");
                cache.Set(key + ":error", error, TimeSpan.FromSeconds(30));
                throw error;
            }
            catch (MarketDataException ex)
            {
                cache.Set(key + ":error", ex, TimeSpan.FromSeconds(30));
                throw;
            }
        }
        finally { gate.Release(); }
    }

    public static PriceQuote ParseQuote(byte[] json, string ticker, DateTimeOffset fetchedAt)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            var chart = document.RootElement.GetProperty("chart");
            if (chart.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null)
                throw new MarketDataException("YAHOO_SYMBOL_ERROR", $"Yahoo did not return a quote for {ticker}.");
            var results = chart.GetProperty("result");
            if (results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0)
                throw new MarketDataException("YAHOO_NO_QUOTE", $"No Yahoo quote is available for {ticker}.");
            var meta = results[0].GetProperty("meta");
            if (!string.Equals(meta.GetProperty("symbol").GetString(), ticker, StringComparison.OrdinalIgnoreCase) ||
                meta.GetProperty("currency").GetString() != "INR")
                throw new MarketDataException("YAHOO_QUOTE_MISMATCH", "Yahoo quote symbol/currency does not match the holding.");
            var price = meta.GetProperty("regularMarketPrice").GetDecimal();
            var time = DateTimeOffset.FromUnixTimeSeconds(meta.GetProperty("regularMarketTime").GetInt64());
            if (price <= 0 || time > fetchedAt.AddMinutes(5))
                throw new MarketDataException("YAHOO_INVALID_QUOTE", "Yahoo returned an invalid price or timestamp.");
            var date = DateOnly.FromDateTime(time.ToOffset(MarketClock.IndiaOffset).DateTime);
            return new PriceQuote(decimal.Round(price, 4), date, "YAHOO", time, fetchedAt);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException or ArgumentOutOfRangeException or OverflowException)
        {
            throw new MarketDataException("YAHOO_INVALID_RESPONSE", "Yahoo returned an unexpected response format.");
        }
    }
}
