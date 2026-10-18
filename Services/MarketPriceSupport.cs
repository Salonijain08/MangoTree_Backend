using System.Text.RegularExpressions;

namespace StockPortfolio.Api.Services;

public static class MarketClock
{
    public static readonly TimeSpan IndiaOffset = TimeSpan.FromMinutes(330);
    public static DateTimeOffset Now => DateTimeOffset.UtcNow.ToOffset(IndiaOffset);
    public static DateOnly Today => DateOnly.FromDateTime(Now.DateTime);
    public static readonly DateOnly FirstUdIfDate = new(2024, 7, 8);
}

public sealed class MarketDataException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record PriceQuote(decimal Price, DateOnly Date, string Source,
    DateTimeOffset? QuoteTimeUtc, DateTimeOffset FetchedAtUtc);

public static class TradingSymbols
{
    // Explicit aliases for the existing Excel names. Never guess arbitrary names.
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GENUS POWER"] = "GENUSPOWER",
        ["GENUS POWER INFRASTRUCTURES"] = "GENUSPOWER",
        ["BSE INDIA"] = "BSE", ["BSEINDIA"] = "BSE", ["BSE LIMITED"] = "BSE",
        ["GREAVES"] = "GREAVESCOT", ["GREAVES COTTON"] = "GREAVESCOT",
        ["GREAVESCOTTON"] = "GREAVESCOT",
        ["TD POWER"] = "TDPOWERSYS", ["TDPOWER"] = "TDPOWERSYS",
        ["TD POWER SYSTEMS"] = "TDPOWERSYS", ["TDPOWERSYSTEMS"] = "TDPOWERSYS"
    };

    public static string Normalize(string? symbol, string? exchange)
    {
        var ex = (exchange ?? "").Trim().ToUpperInvariant();
        if (ex is not ("NSE" or "BSE"))
            throw new MarketDataException("INVALID_EXCHANGE", "Exchange must be NSE or BSE.");
        var value = (symbol ?? "").Trim().ToUpperInvariant();
        if (value.EndsWith(".NS") || value.EndsWith(".BO"))
        {
            if (value.EndsWith(".NS") != (ex == "NSE"))
                throw new MarketDataException("EXCHANGE_MISMATCH", "Symbol suffix and exchange do not match.");
            value = value[..^3];
        }
        if (ex == "NSE" && Aliases.TryGetValue(value, out var alias)) value = alias;
        if (!Regex.IsMatch(value, @"^[A-Z0-9&._-]{1,30}$", RegexOptions.CultureInvariant))
            throw new MarketDataException("INVALID_SYMBOL",
                $"'{symbol}' is not an exact trading symbol. Enter an NSE ticker or BSE scrip code.");
        return value;
    }
}

public static class MarketHttp
{
    public static async Task<byte[]> ReadLimitedAsync(HttpContent content, int maxBytes, CancellationToken ct)
    {
        if (content.Headers.ContentLength > maxBytes)
            throw new MarketDataException("INVALID_RESPONSE", "Market data response is too large.");
        await using var input = await content.ReadAsStreamAsync(ct);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        {
            if (output.Length + count > maxBytes)
                throw new MarketDataException("INVALID_RESPONSE", "Market data response is too large.");
            await output.WriteAsync(buffer.AsMemory(0, count), ct);
        }
        return output.ToArray();
    }
}
