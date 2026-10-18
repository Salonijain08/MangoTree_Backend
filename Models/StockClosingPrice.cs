using System.ComponentModel.DataAnnotations;

namespace StockPortfolio.Api.Models;

public sealed class StockClosingPrice
{
    [MaxLength(30)] public string Symbol { get; set; } = string.Empty;
    [MaxLength(10)] public string Exchange { get; set; } = "NSE";
    [MaxLength(10)] public string Series { get; set; } = "EQ";
    public DateOnly PriceDate { get; set; }
    public decimal ClosingPrice { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}

// Written in the SAME transaction as all rows. Marks a complete daily cache.
public sealed class NseBhavcopyImport
{
    public DateOnly PriceDate { get; set; }
    public int RowCount { get; set; }
    public DateTimeOffset FetchedAtUtc { get; set; }
}
