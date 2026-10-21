namespace StockPortfolio.Api.DTOs;


// =========================================================
// HOLDING REQUESTS
// =========================================================

public record CreateHoldingRequest(
    string Symbol,
    string Exchange,
    string? CompanyName,
    string? InstrumentKey,
    DateOnly BuyingDate,
    decimal BuyingQuantity,
    decimal BuyingPrice);


public record UpdateHoldingRequest(
    string Symbol,
    string Exchange,
    string? CompanyName,
    string? InstrumentKey,
    DateOnly BuyingDate,
    decimal BuyingQuantity,
    decimal BuyingPrice);


// =========================================================
// SALE REQUESTS
// =========================================================

public record CreateSaleRequest(
    DateOnly SellingDate,
    decimal SellingQuantity,
    decimal SellingPrice);


public record UpdateSaleRequest(
    DateOnly SellingDate,
    decimal SellingQuantity,
    decimal SellingPrice);


// =========================================================
// PURCHASE RESPONSE
// =========================================================

public sealed record PurchaseResponse(
    int Id,
    int SerialNo,
    DateOnly BuyingDate,
    decimal BuyingQuantity,
    decimal BuyingPrice,
    decimal SoldQuantity,
    decimal RemainingQuantity,
    decimal BuyValue,
    decimal RemainingBuyValue,
    decimal? CurrentPrice,
    decimal? CurrentValue,
    decimal? UnrealizedProfitLoss,
    decimal? ProfitLossPercentage);


// =========================================================
// FIFO SALE ALLOCATION RESPONSE
// =========================================================

public sealed record SaleAllocationResponse(
    int PurchaseId,
    int SerialNo,
    DateOnly BuyingDate,
    decimal Quantity,
    decimal BuyingPrice,
    decimal BuyValue,
    int HoldingDays,
    string Term);


// =========================================================
// SALE RESPONSE
// =========================================================

public sealed record SaleResponse(
    int Id,
    DateOnly SellingDate,
    decimal SellingQuantity,
    decimal SellingPrice,
    decimal BuyPrice,
    decimal BuyValue,
    decimal SaleValue,
    decimal RealizedProfitLoss)
{
    public IReadOnlyCollection<SaleAllocationResponse> FifoAllocations
    {
        get;
        init;
    } = Array.Empty<SaleAllocationResponse>();
}


// =========================================================
// HOLDING RESPONSE
// =========================================================

public sealed record HoldingResponse(
    int Id,
    string Symbol,
    string Exchange,
    string? CompanyName,
    string? InstrumentKey,
    DateOnly BuyingDate,
    decimal BuyingQuantity,
    decimal BuyingPrice,
    decimal SoldQuantity,
    decimal RemainingQuantity,
    decimal? LatestClosingPrice,
    DateOnly? ClosingPriceDate,
    decimal Investment,
    decimal RemainingCost,
    decimal? CurrentValue,
    decimal RealizedProfitLoss,
    decimal? UnrealizedProfitLoss,
    decimal? TotalProfitLoss,
    decimal? ProfitLossPercentage,
    bool IsFullySold,
    IReadOnlyCollection<SaleResponse> Sales)
{
    public decimal AverageBuyingPrice =>
        BuyingPrice;


    public IReadOnlyCollection<PurchaseResponse> Purchases
    {
        get;
        init;
    } = Array.Empty<PurchaseResponse>();


    public decimal? LatestTradePrice
    {
        get;
        init;
    }


    public DateTimeOffset? LatestTradePriceTimeUtc
    {
        get;
        init;
    }


    public DateTimeOffset? LatestTradePriceFetchedAtUtc
    {
        get;
        init;
    }


    public decimal? DisplayPrice
    {
        get;
        init;
    }


    public string PriceSource
    {
        get;
        init;
    } = "UNAVAILABLE";


    public DateOnly? PriceDate
    {
        get;
        init;
    }


    public DateTimeOffset? PriceTimeUtc
    {
        get;
        init;
    }


    public DateTimeOffset? FetchedAtUtc
    {
        get;
        init;
    }


    public DateOnly ValuationDate
    {
        get;
        init;
    }


    public string PriceStatus
    {
        get;
        init;
    } = "UNAVAILABLE";


    public string? PriceMessage
    {
        get;
        init;
    }


    public bool IsHistorical
    {
        get;
        init;
    }


    // Weighted average selling rate of all sold quantity
    public decimal? AverageSoldRate
    {
        get;
        init;
    }
}


// =========================================================
// SOLD STOCK RESPONSE
// =========================================================

public sealed record SoldStockResponse(
    int SaleId,
    int HoldingId,
    string Symbol,
    string CompanyName,
    string Exchange,
    DateOnly BuyingDate,
    decimal BuyingPrice,
    DateOnly SellingDate,
    decimal SellingQuantity,
    decimal SellingPrice,
    decimal BuyValue,
    decimal SaleValue,
    decimal ProfitLoss,
    DateTime CreatedAtUtc);