namespace StockPortfolio.Api.DTOs;

public sealed record DashboardResponse(
    DashboardSummaryResponse Summary,
    IReadOnlyCollection<DashboardHoldingResponse> TopProfitHoldings,
    IReadOnlyCollection<DashboardHoldingResponse> TopLossHoldings,
    IReadOnlyCollection<DashboardSoldStockResponse> RecentSales);

public sealed record DashboardSummaryResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    int ActiveHoldings,
    int SoldTransactions,
    decimal TotalInvestment,
    decimal TotalCurrentValue,
    decimal TotalUnrealizedProfitLoss,
    decimal TotalRealizedProfitLoss,
    decimal TotalProfitLoss,
    decimal ProfitLossPercentage,
    decimal TotalActiveQuantity,
    decimal TotalSoldQuantity,
    decimal TotalBuyValue,
    decimal TotalSaleValue,
    int ProfitSales,
    int LossSales);

public sealed record DashboardHoldingResponse(
    int Id,
    string Symbol,
    string? CompanyName,
    string Exchange,
    decimal Quantity,
    decimal BuyingPrice,
    decimal? CurrentPrice,
    decimal Investment,
    decimal CurrentValue,
    decimal ProfitLoss,
    decimal ProfitLossPercentage);

public sealed record DashboardSoldStockResponse(
    int SaleId,
    int HoldingId,
    string Symbol,
    string? CompanyName,
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