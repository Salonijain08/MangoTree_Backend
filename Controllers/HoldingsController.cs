using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.DTOs;
using StockPortfolio.Api.Helpers;
using StockPortfolio.Api.Models;
using StockPortfolio.Api.Services;

namespace StockPortfolio.Api.Controllers;

[ApiController, Route("api/holdings"), Authorize]
public sealed class HoldingsController(
    AppDbContext db,
    IExcelImportService excel,
    IOneDriveExcelService oneDriveExcel,
    HoldingsPriceService prices,
    ILogger<HoldingsController> logger) : ControllerBase
{
    // =========================================================
    // GET ALL HOLDINGS
    // =========================================================

    [HttpGet]
    public async Task<IActionResult> GetAll(
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        try
        {
            return Ok(
                (await prices.GetAsync(
                    User.GetUserId(),
                    date,
                    false,
                    null,
                    ct)).Items);
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }
    }


    // =========================================================
    // GET PRICES
    // =========================================================

    [HttpGet("prices")]
    public async Task<IActionResult> GetPrices(
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        try
        {
            return Ok(
                await prices.GetAsync(
                    User.GetUserId(),
                    date,
                    false,
                    null,
                    ct));
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }
    }


    // =========================================================
    // GET HOLDING BY ID
    // =========================================================

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Get(int id)
    {
        var item = await Owned()
            .SingleOrDefaultAsync(
                x => x.Id == id);

        return item is null
            ? NotFound()
            : Ok(item.ToResponse());
    }


    // =========================================================
    // CREATE HOLDING / PURCHASE
    // =========================================================

    [HttpPost]
    public async Task<IActionResult> Create(
        CreateHoldingRequest request)
    {
        var error = ValidateHolding(
            request.BuyingQuantity,
            request.BuyingPrice);

        if (error is not null)
        {
            return BadRequest(
                new
                {
                    message = error
                });
        }


        string symbol;

        try
        {
            symbol = TradingSymbols.Normalize(
                request.Symbol,
                request.Exchange);
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }


        var userId = User.GetUserId();

        var exchange = request.Exchange
            .Trim()
            .ToUpperInvariant();


        // =====================================================
        // CHECK WHETHER SAME STOCK ALREADY EXISTS
        // =====================================================

        var existing = await Owned()
            .SingleOrDefaultAsync(
                x =>
                    x.Symbol == symbol &&
                    x.Exchange == exchange);


        // =====================================================
        // EXISTING HOLDING
        // =====================================================

        if (existing is not null)
        {
            var serial =
                await GetNextSerialNo(userId);


            var purchase = new StockPurchase
            {
                UserId = userId,

                StockHoldingId = existing.Id,

                // Normal display/internal serial
                SerialNo = serial,

                // Manual POST purchase
                ExcelSerialNo = null,

                // Purchase source
                ImportSource = "MANUAL",

                BuyingDate =
                    request.BuyingDate,

                BuyingQuantity =
                    request.BuyingQuantity,

                BuyingPrice =
                    request.BuyingPrice,

                CreatedAtUtc =
                    DateTime.UtcNow
            };


            db.StockPurchases.Add(
                purchase);

            existing.Purchases.Add(
                purchase);


            // -------------------------------------------------
            // Update company name if provided
            // -------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    request.CompanyName))
            {
                existing.CompanyName =
                    request.CompanyName.Trim();
            }


            // -------------------------------------------------
            // Update instrument key if provided
            // -------------------------------------------------

            if (!string.IsNullOrWhiteSpace(
                    request.InstrumentKey))
            {
                existing.InstrumentKey =
                    request.InstrumentKey.Trim();
            }


            // -------------------------------------------------
            // Recalculate weighted average
            // -------------------------------------------------

            HoldingAggregationService.Recalculate(
                existing);


            await db.SaveChangesAsync();


            // -------------------------------------------------
            // Load sales before response
            // -------------------------------------------------

            await db.Entry(existing)
                .Collection(x => x.Sales)
                .LoadAsync();


            return Ok(
                existing.ToResponse());
        }


        // =====================================================
        // NEW HOLDING
        // =====================================================

        var item = new StockHolding
        {
            UserId =
                userId,

            Symbol =
                symbol,

            Exchange =
                exchange,

            CompanyName =
                request.CompanyName?.Trim(),

            InstrumentKey =
                request.InstrumentKey?.Trim(),

            BuyingDate =
                request.BuyingDate,

            BuyingQuantity =
                request.BuyingQuantity,

            BuyingPrice =
                request.BuyingPrice
        };


        // =====================================================
        // MANUAL PURCHASE
        // =====================================================

        var newPurchase = new StockPurchase
        {
            UserId =
                userId,

            SerialNo =
                await GetNextSerialNo(userId),

            // Manual POST purchase
            ExcelSerialNo = null,

            // Purchase source
            ImportSource = "MANUAL",

            BuyingDate =
                request.BuyingDate,

            BuyingQuantity =
                request.BuyingQuantity,

            BuyingPrice =
                request.BuyingPrice,

            CreatedAtUtc =
                DateTime.UtcNow,

            StockHolding =
                item
        };


        item.Purchases.Add(
            newPurchase);

        db.StockHoldings.Add(
            item);


        await db.SaveChangesAsync();


        return CreatedAtAction(
            nameof(Get),
            new { id = item.Id },
            item.ToResponse());
    }


    // =========================================================
    // UPDATE HOLDING
    // =========================================================

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(
        int id,
        UpdateHoldingRequest request)
    {
        var item = await Owned()
            .SingleOrDefaultAsync(
                x => x.Id == id);


        if (item is null)
        {
            return NotFound();
        }


        var error = ValidateHolding(
            request.BuyingQuantity,
            request.BuyingPrice);


        if (error is not null)
        {
            return BadRequest(
                new
                {
                    message = error
                });
        }


        // -----------------------------------------------------
        // Multiple purchases cannot be edited as one holding
        // -----------------------------------------------------

        if (item.Purchases.Count > 1)
        {
            return BadRequest(
                new
                {
                    message =
                        "This holding contains multiple buying transactions. " +
                        "Add new purchases through the Excel sync so the buying " +
                        "history and average price remain accurate."
                });
        }


        string symbol;

        try
        {
            symbol = TradingSymbols.Normalize(
                request.Symbol,
                request.Exchange);
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }


        var exchange = request.Exchange
            .Trim()
            .ToUpperInvariant();


        // =====================================================
        // CLEAR CACHED PRICES IF SYMBOL / EXCHANGE CHANGES
        // =====================================================

        if (
            item.Symbol != symbol ||
            item.Exchange != exchange)
        {
            item.LatestTradePrice = null;

            item.LatestTradePriceTimeUtc = null;

            item.LatestTradePriceFetchedAtUtc = null;

            item.LatestClosingPrice = null;

            item.ClosingPriceDate = null;

            item.ClosingPriceSource = null;

            item.ClosingPriceFetchedAtUtc = null;
        }


        item.Symbol =
            symbol;

        item.Exchange =
            exchange;

        item.CompanyName =
            request.CompanyName?.Trim();

        item.InstrumentKey =
            request.InstrumentKey?.Trim();


        // =====================================================
        // GET EXISTING PURCHASE
        // =====================================================

        var purchase =
            item.Purchases.SingleOrDefault();


        // =====================================================
        // CREATE PURCHASE IF NONE EXISTS
        // =====================================================

        if (purchase is null)
        {
            purchase = new StockPurchase
            {
                UserId =
                    item.UserId,

                StockHoldingId =
                    item.Id,

                SerialNo =
                    await GetNextSerialNo(
                        item.UserId),

                // Manual update
                ExcelSerialNo = null,

                ImportSource = "MANUAL"
            };


            item.Purchases.Add(
                purchase);

            db.StockPurchases.Add(
                purchase);
        }


        // =====================================================
        // UPDATE PURCHASE DETAILS
        // =====================================================

        purchase.BuyingDate =
            request.BuyingDate;

        purchase.BuyingQuantity =
            request.BuyingQuantity;

        purchase.BuyingPrice =
            request.BuyingPrice;


        // =====================================================
        // RECALCULATE HOLDING
        // =====================================================

        HoldingAggregationService.Recalculate(
            item);


        await db.SaveChangesAsync();


        return Ok(
            item.ToResponse());
    }


    // =========================================================
    // DELETE HOLDING
    // =========================================================

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(
        int id)
    {
        var item = await Owned()
            .SingleOrDefaultAsync(
                x => x.Id == id);


        if (item is null)
        {
            return NotFound(
                new
                {
                    message =
                        "Holding not found."
                });
        }


        if (item.Sales.Count > 0)
        {
            db.StockSales.RemoveRange(
                item.Sales);
        }


        db.StockHoldings.Remove(
            item);


        await db.SaveChangesAsync();


        return Ok(
            new
            {
                message =
                    "Holding deleted successfully."
            });
    }


    // =========================================================
    // OLD EXCEL UPLOAD
    // =========================================================
    //
    // This endpoint is kept temporarily.
    //
    // Final frontend will NOT use this endpoint.
    //
    // =========================================================

    [HttpPost("import-excel")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> ImportExcel(
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null)
        {
            return BadRequest(
                new
                {
                    message =
                        "Please select an Excel file."
                });
        }


        if (file.Length == 0)
        {
            return BadRequest(
                new
                {
                    message =
                        "The uploaded Excel file is empty."
                });
        }


        if (!Path.GetExtension(
                file.FileName)
            .Equals(
                ".xlsx",
                StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(
                new
                {
                    message =
                        "Only .xlsx Excel files are supported."
                });
        }


        try
        {
            await using var stream =
                file.OpenReadStream();


            var rows =
                excel.Read(stream);


            return Ok(
                await SyncExcelRows(
                    rows,
                    ct));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(
                new
                {
                    message =
                        ex.Message
                });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(
                ex,
                "Excel import database update failed for user {UserId}",
                User.GetUserId());


            return Conflict(
                new
                {
                    message =
                        "Excel sync could not be completed because " +
                        "one or more records already exist. " +
                        "Please try again."
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Excel import failed for user {UserId}",
                User.GetUserId());


            return StatusCode(
                500,
                new
                {
                    message =
                        "Excel import failed. Please try again."
                });
        }
    }


    // =========================================================
    // SYNC EXCEL FROM MASTER FILE
    // =========================================================

    [HttpPost("sync-excel")]
    public async Task<IActionResult> SyncExcel(
        CancellationToken ct)
    {
        try
        {
            // -------------------------------------------------
            // Download master Excel
            // -------------------------------------------------

            await using var excelStream =
                await oneDriveExcel
                    .DownloadExcelAsync(ct);


            // -------------------------------------------------
            // Read Excel
            // -------------------------------------------------

            var rows =
                excel.Read(excelStream);


            // -------------------------------------------------
            // Sync rows
            // -------------------------------------------------

            return Ok(
                await SyncExcelRows(
                    rows,
                    ct));
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(
                ex,
                "OneDrive Excel sync validation failed for user {UserId}",
                User.GetUserId());


            return BadRequest(
                new
                {
                    message =
                        ex.Message
                });
        }
        catch (DbUpdateException ex)
        {
            logger.LogError(
                ex,
                "OneDrive Excel sync database update failed for user {UserId}",
                User.GetUserId());


            return Conflict(
                new
                {
                    message =
                        "Excel sync could not be completed because " +
                        "one or more records already exist. " +
                        "Please try the sync again."
                });
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "OneDrive Excel sync failed for user {UserId}",
                User.GetUserId());


            return StatusCode(
                500,
                new
                {
                    message =
                        "Excel sync failed while reading " +
                        "the OneDrive Excel file. Please try again."
                });
        }
    }


    // =========================================================
    // COMMON EXCEL SYNC LOGIC
    // =========================================================

    private async Task<object> SyncExcelRows(
        IReadOnlyList<ExcelPurchaseRow> rows,
        CancellationToken ct)
    {
        await using var transaction =
            await db.Database
                .BeginTransactionAsync(ct);


        var userId =
            User.GetUserId();


        // =====================================================
        // GET ALREADY IMPORTED EXCEL SERIAL NUMBERS
        // =====================================================
        //
        // IMPORTANT:
        //
        // We DO NOT check SerialNo here.
        //
        // Only ExcelSerialNo identifies an Excel import.
        //
        // Manual purchases have:
        //
        //     ExcelSerialNo = null
        //
        // Therefore manual SerialNo cannot block Excel sync.
        //
        // =====================================================

        var existingExcelSerials =
            (
                await db.StockPurchases
                    .Where(x =>
                        x.UserId == userId &&
                        x.ExcelSerialNo.HasValue)
                    .Select(x =>
                        x.ExcelSerialNo!.Value)
                    .ToListAsync(ct)
            )
            .ToHashSet();


        // =====================================================
        // LOAD EXISTING HOLDINGS + PURCHASES
        // =====================================================

        var existingHoldings =
            await db.StockHoldings
                .Include(x =>
                    x.Purchases)
                .Where(x =>
                    x.UserId == userId)
                .ToListAsync(ct);


        var imported = 0;

        var skipped = 0;

        var newlyCreatedHoldings = 0;


        // =====================================================
        // PROCESS EXCEL ROWS
        // =====================================================

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();


            // =================================================
            // EXCEL SERIAL ALREADY IMPORTED
            // =================================================

            if (existingExcelSerials.Contains(
                    row.SerialNo))
            {
                skipped++;

                continue;
            }


            // =================================================
            // FIND SAME HOLDING
            // =================================================
            //
            // Same Symbol + Exchange = same holding
            //
            // =================================================

            var holding =
                existingHoldings
                    .SingleOrDefault(
                        x =>
                            x.Symbol ==
                                row.Symbol &&
                            x.Exchange ==
                                row.Exchange);


            // =================================================
            // CREATE NEW HOLDING
            // =================================================

            if (holding is null)
            {
                holding = new StockHolding
                {
                    UserId =
                        userId,

                    Symbol =
                        row.Symbol,

                    Exchange =
                        row.Exchange,

                    CompanyName =
                        row.CompanyName,

                    InstrumentKey =
                        row.InstrumentKey,

                    BuyingDate =
                        row.BuyingDate,

                    BuyingQuantity =
                        0m,

                    BuyingPrice =
                        0m,

                    CreatedAtUtc =
                        DateTime.UtcNow,

                    UpdatedAtUtc =
                        DateTime.UtcNow
                };


                db.StockHoldings.Add(
                    holding);


                existingHoldings.Add(
                    holding);


                newlyCreatedHoldings++;
            }
            else
            {
                // -------------------------------------------------
                // Excel doesn't contain Company Name.
                //
                // Therefore don't overwrite an existing name.
                // -------------------------------------------------

                if (
                    string.IsNullOrWhiteSpace(
                        holding.CompanyName))
                {
                    holding.CompanyName =
                        row.CompanyName;
                }
            }


            // =================================================
            // CREATE PURCHASE HISTORY
            // =================================================

            var purchase = new StockPurchase
            {
                UserId =
                    userId,

                // -------------------------------------------------
                // Display / internal serial
                // -------------------------------------------------

                SerialNo =
                    row.SerialNo,

                // -------------------------------------------------
                // Excel sync identifier
                // -------------------------------------------------

                ExcelSerialNo =
                    row.SerialNo,

                // -------------------------------------------------
                // Purchase source
                // -------------------------------------------------

                ImportSource =
                    "EXCEL",

                BuyingDate =
                    row.BuyingDate,

                BuyingQuantity =
                    row.BuyingQuantity,

                BuyingPrice =
                    row.BuyingPrice,

                CreatedAtUtc =
                    DateTime.UtcNow,

                StockHolding =
                    holding
            };


            holding.Purchases.Add(
                purchase);

            db.StockPurchases.Add(
                purchase);


            // =================================================
            // RECALCULATE HOLDING
            // =================================================

            HoldingAggregationService
                .Recalculate(
                    holding);


            // -------------------------------------------------
            // Remember this Excel Serial in current sync
            // -------------------------------------------------

            existingExcelSerials.Add(
                row.SerialNo);


            imported++;
        }


        // =====================================================
        // NO NEW PURCHASES
        // =====================================================

        if (imported == 0)
        {
            await transaction.CommitAsync(
                ct);


            return new
            {
                message =
                    "Excel sync completed. " +
                    "No new stock purchases were found.",

                imported = 0,

                skipped,

                newlyCreatedHoldings = 0,

                totalRowsRead =
                    rows.Count
            };
        }


        // =====================================================
        // SAVE DATABASE
        // =====================================================

        await db.SaveChangesAsync(
            ct);


        await transaction.CommitAsync(
            ct);


        return new
        {
            message =
                $"{imported} new stock purchase(s) " +
                "imported successfully.",

            imported,

            skipped,

            newlyCreatedHoldings,

            totalRowsRead =
                rows.Count
        };
    }


    // =========================================================
    // SYNC SINGLE STOCK CLOSE
    // =========================================================

    [HttpPost("{id:int}/sync-close")]
    public async Task<IActionResult> SyncClose(
        int id,
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        try
        {
            var result =
                await prices.GetAsync(
                    User.GetUserId(),
                    date,
                    true,
                    id,
                    ct);


            return result.Items.Count == 0
                ? NotFound()
                : Ok(result.Items[0]);
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }
    }


    // =========================================================
    // SYNC ALL CLOSES
    // =========================================================

    [HttpPost("sync-all-closes")]
    public async Task<IActionResult> SyncAll(
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        try
        {
            return Ok(
                await prices.GetAsync(
                    User.GetUserId(),
                    date,
                    true,
                    null,
                    ct));
        }
        catch (MarketDataException ex)
        {
            return PriceError(ex);
        }
    }


    // =========================================================
    // GET NEXT SERIAL NUMBER
    // =========================================================

    private async Task<int> GetNextSerialNo(
        int userId)
    {
        var max =
            await db.StockPurchases
                .Where(x =>
                    x.UserId == userId)
                .Select(x =>
                    (int?)x.SerialNo)
                .MaxAsync() ?? 0;


        return Math.Max(
            0,
            max) + 1;
    }


    // =========================================================
    // PRICE ERROR
    // =========================================================

    private BadRequestObjectResult PriceError(
        MarketDataException ex)
    {
        return BadRequest(
            new
            {
                code =
                    ex.Code,

                message =
                    ex.Message
            });
    }


    // =========================================================
    // OWNED HOLDINGS
    // =========================================================

    private IQueryable<StockHolding> Owned()
    {
        return db.StockHoldings
            .Include(x =>
                x.Sales)
            .Include(x =>
                x.Purchases)
            .Where(x =>
                x.UserId ==
                    User.GetUserId());
    }


    // =========================================================
    // VALIDATE HOLDING
    // =========================================================

    private static string? ValidateHolding(
        decimal qty,
        decimal price)
    {
        return qty <= 0
            ? "Buying quantity must be greater than zero."
            : price <= 0
                ? "Buying price must be greater than zero."
                : null;
    }
}