namespace StockPortfolio.Api.Models;

public sealed class StockPurchase
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int StockHoldingId { get; set; }

    public StockHolding StockHolding { get; set; } = null!;


    // =========================================================
    // DISPLAY / INTERNAL SERIAL NUMBER
    // =========================================================

    public int SerialNo { get; set; }


    // =========================================================
    // EXCEL SERIAL NUMBER
    // =========================================================
    //
    // This value is used ONLY for Excel sync.
    //
    // Excel purchase:
    //     ExcelSerialNo = Excel Serial No
    //
    // Manual POST purchase:
    //     ExcelSerialNo = null
    //
    // This keeps manual purchases separate from Excel imports.
    // =========================================================

    public int? ExcelSerialNo { get; set; }


    // =========================================================
    // PURCHASE SOURCE
    // =========================================================
    //
    // EXCEL  -> Imported from Excel
    // MANUAL -> Added manually through API
    //
    // =========================================================

    public string ImportSource { get; set; } = "MANUAL";


    // =========================================================
    // PURCHASE DETAILS
    // =========================================================

    public DateOnly BuyingDate { get; set; }

    public decimal BuyingQuantity { get; set; }

    public decimal BuyingPrice { get; set; }


    // =========================================================
    // CREATED DATE
    // =========================================================

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public ICollection<StockSaleAllocation> SaleAllocations { get; set; }
    = new List<StockSaleAllocation>();
}