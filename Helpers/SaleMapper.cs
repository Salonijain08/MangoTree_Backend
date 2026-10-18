using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Helpers;

public static class SaleMapper
{
    public static SoldStockResponse ToSaleResponse(this StockSale sale, StockHolding holding)
    {
        var companyName = string.IsNullOrWhiteSpace(holding.CompanyName)
            ? holding.Symbol
            : holding.CompanyName;

        var buyPrice = sale.BuyPriceAtSale ?? holding.BuyingPrice;
        var buyValue = buyPrice * sale.SellingQuantity;
        var saleValue = sale.SellingPrice * sale.SellingQuantity;

        return new SoldStockResponse(
            sale.Id,
            holding.Id,
            holding.Symbol,
            companyName,
            holding.Exchange,
            holding.BuyingDate,
            buyPrice,
            sale.SellingDate,
            sale.SellingQuantity,
            sale.SellingPrice,
            buyValue,
            saleValue,
            saleValue - buyValue,
            sale.CreatedAtUtc);
    }
}
