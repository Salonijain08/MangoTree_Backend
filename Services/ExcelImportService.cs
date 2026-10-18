using System.Globalization;
using ClosedXML.Excel;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Services;

public sealed record ExcelPurchaseRow(
    int SerialNo,
    string CompanyName,
    string Symbol,
    string Exchange,
    DateOnly BuyingDate,
    decimal BuyingQuantity,
    decimal BuyingPrice,
    string? InstrumentKey);

public interface IExcelImportService
{
    IReadOnlyList<ExcelPurchaseRow> Read(Stream stream);
}

public sealed class ExcelImportService : IExcelImportService
{
    // =========================================================
    // EXCEL COLUMN ALIASES
    // =========================================================

    private static readonly string[] SerialAliases =
    [
        "serial no",
        "serial number",
        "serial",
        "sr no",
        "sr no.",
        "sr. no",
        "s no",
        "s.no",
        "id",
        "unique id"
    ];

    private static readonly string[] SymbolAliases =
    [
        "symbol",
        "stock symbol",
        "scrip",
        "ticker",
        "stock",
        "stock name"
    ];

    private static readonly string[] ExchangeAliases =
    [
        "exchange",
        "stock exchange"
    ];

    private static readonly string[] DateAliases =
    [
        "date",
        "buy date",
        "buying date",
        "purchase date"
    ];

    private static readonly string[] PriceAliases =
    [
        "buy price",
        "buying price",
        "purchase price",
        "rate"
    ];

    private static readonly string[] QuantityAliases =
    [
        "quantity",
        "buy quantity",
        "buying quantity",
        "purchase quantity",
        "qty",
        "quet"
    ];


    // =========================================================
    // READ EXCEL
    // =========================================================

    public IReadOnlyList<ExcelPurchaseRow> Read(Stream stream)
    {
        try
        {
            using var workbook = new XLWorkbook(stream);

            var sheet = workbook.Worksheets.FirstOrDefault()
                ?? throw new InvalidOperationException(
                    "The Excel file does not contain a worksheet.");

            var used = sheet.RangeUsed()
                ?? throw new InvalidOperationException(
                    "The Excel worksheet is empty.");

            // -------------------------------------------------
            // Find header row
            // -------------------------------------------------

            var headerRowNumber = FindHeaderRow(used);

            if (headerRowNumber == 0)
            {
                throw new InvalidOperationException(
                    "The header row was not found. " +
                    "Required columns are SERIAL NO, SYMBOL, EXCHANGE, " +
                    "BUYING DATE, QUANTITY and BUY PRICE.");
            }

            // -------------------------------------------------
            // Read headers
            // -------------------------------------------------

            var headers = sheet
                .Row(headerRowNumber)
                .CellsUsed()
                .Select(cell => new
                {
                    Name = Normalize(cell.GetString()),
                    Column = cell.Address.ColumnNumber
                })
                .Where(x => x.Name.Length > 0)
                .GroupBy(
                    x => x.Name,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    x => x.Key,
                    x => x.Select(y => y.Column).ToList(),
                    StringComparer.OrdinalIgnoreCase);

            // -------------------------------------------------
            // Find required columns
            // -------------------------------------------------

            var serialColumn = FindColumn(
                headers,
                SerialAliases);

            var symbolColumn = FindColumn(
                headers,
                SymbolAliases);

            var exchangeColumn = FindColumn(
                headers,
                ExchangeAliases);

            var dateColumn = FindColumn(
                headers,
                DateAliases);

            var priceColumn = FindColumn(
                headers,
                PriceAliases);

            var quantityColumn = FindColumn(
                headers,
                QuantityAliases);


            // -------------------------------------------------
            // Read rows
            // -------------------------------------------------

            var rows = new List<ExcelPurchaseRow>();

            var errors = new List<string>();

            // Prevent duplicate Serial No inside the same Excel
            var serials = new HashSet<int>();


            for (
                var rowNumber = headerRowNumber + 1;
                rowNumber <= used.LastRow().RowNumber();
                rowNumber++)
            {
                var row = sheet.Row(rowNumber);


                // =================================================
                // SYMBOL
                // =================================================

                var symbolText = row
                    .Cell(symbolColumn)
                    .GetFormattedString()
                    .Trim();

                // Completely empty row -> ignore
                if (string.IsNullOrWhiteSpace(symbolText))
                {
                    continue;
                }


                // =================================================
                // SERIAL NO
                // =================================================

                if (!TryReadInt(
                        row.Cell(serialColumn),
                        out var serialNo) ||
                    serialNo <= 0)
                {
                    errors.Add(
                        $"Row {rowNumber}: Serial No must be a positive whole number.");

                    continue;
                }


                // Duplicate Serial No inside Excel
                if (!serials.Add(serialNo))
                {
                    errors.Add(
                        $"Row {rowNumber}: Serial No {serialNo} is duplicated in the Excel file.");

                    continue;
                }


                // =================================================
                // EXCHANGE
                // =================================================

                var exchange = row
                    .Cell(exchangeColumn)
                    .GetFormattedString()
                    .Trim()
                    .ToUpperInvariant();

                if (string.IsNullOrWhiteSpace(exchange))
                {
                    exchange = "NSE";
                }


                // =================================================
                // BUYING DATE
                // =================================================

                if (!TryReadDate(
                        row.Cell(dateColumn),
                        out var buyingDate))
                {
                    errors.Add(
                        $"Row {rowNumber}: Buying date for symbol '{symbolText}' is missing or invalid.");

                    continue;
                }


                // =================================================
                // BUY PRICE
                // =================================================

                if (!TryReadDecimal(
                        row.Cell(priceColumn),
                        out var buyingPrice) ||
                    buyingPrice <= 0)
                {
                    errors.Add(
                        $"Row {rowNumber}: Buy price for symbol '{symbolText}' must be greater than zero.");

                    continue;
                }


                // =================================================
                // QUANTITY
                // =================================================

                if (!TryReadDecimal(
                        row.Cell(quantityColumn),
                        out var buyingQuantity) ||
                    buyingQuantity <= 0)
                {
                    errors.Add(
                        $"Row {rowNumber}: Quantity for symbol '{symbolText}' must be greater than zero.");

                    continue;
                }


                // =================================================
                // NORMALIZE SYMBOL
                // =================================================

                string symbol;

                try
                {
                    symbol = TradingSymbols.Normalize(
                        symbolText,
                        exchange);
                }
                catch (MarketDataException ex)
                {
                    errors.Add(
                        $"Row {rowNumber}: {ex.Message}");

                    continue;
                }


                // =================================================
                // COMPANY NAME
                // =================================================
                //
                // Your Excel does NOT have a Company Name column.
                //
                // Therefore we use SYMBOL as CompanyName for now.
                //
                // Example:
                // SYMBOL = ATHERENERG
                // CompanyName = ATHERENERG
                //
                // This does NOT affect stock grouping because the
                // actual holding grouping is done using Symbol + Exchange.
                //
                // =================================================

                var companyName = symbol;


                // =================================================
                // ADD PURCHASE ROW
                // =================================================

                rows.Add(
                    new ExcelPurchaseRow(
                        SerialNo: serialNo,
                        CompanyName: companyName,
                        Symbol: symbol,
                        Exchange: exchange,
                        BuyingDate: buyingDate,
                        BuyingQuantity: buyingQuantity,
                        BuyingPrice: buyingPrice,
                        InstrumentKey: null));
            }


            // =====================================================
            // VALIDATION ERRORS
            // =====================================================

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Excel validation failed. " +
                    string.Join(" ", errors.Take(10)));
            }


            // =====================================================
            // NO VALID ROWS
            // =====================================================

            if (rows.Count == 0)
            {
                throw new InvalidOperationException(
                    "No valid stock purchase records were found in the Excel file.");
            }


            return rows;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"The Excel file could not be read: {ex.Message}");
        }
    }


    // =========================================================
    // FIND HEADER ROW
    // =========================================================

    private static int FindHeaderRow(IXLRange used)
    {
        foreach (var row in used.RowsUsed().Take(20))
        {
            var values = row
                .CellsUsed()
                .Select(x => Normalize(x.GetString()))
                .ToList();

            // Your Excel requires:
            //
            // SERIAL NO
            // SYMBOL
            // EXCHANGE
            // BUYING DATE
            // QUANTITY
            // BUY PRICE
            //
            // Company Name is NOT required.

            if (
                HasAlias(values, SerialAliases) &&
                HasAlias(values, SymbolAliases) &&
                HasAlias(values, ExchangeAliases) &&
                HasAlias(values, DateAliases) &&
                HasAlias(values, PriceAliases) &&
                HasAlias(values, QuantityAliases))
            {
                return row.RowNumber();
            }
        }

        return 0;
    }


    // =========================================================
    // CHECK HEADER ALIAS
    // =========================================================

    private static bool HasAlias(
        IEnumerable<string> values,
        IEnumerable<string> aliases)
    {
        var expected = aliases
            .Select(Normalize)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return values.Any(expected.Contains);
    }


    // =========================================================
    // FIND COLUMN
    // =========================================================

    private static int FindColumn(
        IReadOnlyDictionary<string, List<int>> headers,
        IEnumerable<string> aliases,
        bool required = true)
    {
        foreach (var alias in aliases)
        {
            var normalizedAlias = Normalize(alias);

            if (
                headers.TryGetValue(
                    normalizedAlias,
                    out var columns) &&
                columns.Count > 0)
            {
                return columns[0];
            }
        }


        if (required)
        {
            throw new InvalidOperationException(
                $"Required Excel column is missing: {aliases.First()}.");
        }


        return 0;
    }


    // =========================================================
    // READ INTEGER
    // =========================================================

    private static bool TryReadInt(
        IXLCell cell,
        out int value)
    {
        if (cell.TryGetValue<int>(out value))
        {
            return true;
        }

        var text = cell
            .GetFormattedString()
            .Trim();

        return int.TryParse(
            text,
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out value);
    }


    // =========================================================
    // READ DECIMAL
    // =========================================================

    private static bool TryReadDecimal(
        IXLCell cell,
        out decimal value)
    {
        if (cell.TryGetValue<decimal>(out value))
        {
            return true;
        }

        var text = cell
            .GetFormattedString()
            .Replace(",", "")
            .Replace("₹", "")
            .Trim();

        return decimal.TryParse(
            text,
            NumberStyles.Any,
            CultureInfo.InvariantCulture,
            out value);
    }


    // =========================================================
    // READ DATE
    // =========================================================

    private static bool TryReadDate(
        IXLCell cell,
        out DateOnly value)
    {
        value = default;


        // -------------------------------------------------
        // Excel native DateTime
        // -------------------------------------------------

        if (cell.TryGetValue<DateTime>(out var date))
        {
            value = DateOnly.FromDateTime(date);

            return true;
        }


        // -------------------------------------------------
        // Excel DateTimeOffset
        // -------------------------------------------------

        if (cell.TryGetValue<DateTimeOffset>(
                out var dateOffset))
        {
            value = DateOnly.FromDateTime(
                dateOffset.DateTime);

            return true;
        }


        // -------------------------------------------------
        // String date
        // -------------------------------------------------

        var text = cell
            .GetFormattedString()
            .Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }


        var formats = new[]
        {
            "dd/MM/yyyy",
            "dd/MM/yy",
            "d/M/yyyy",
            "d/M/yy",

            "yyyy-MM-dd",

            "dd-MM-yyyy",
            "dd-MM-yy",

            "d-M-yyyy",
            "d-M-yy",

            "MM/dd/yyyy",
            "M/d/yyyy"
        };


        if (
            DateTime.TryParseExact(
                text,
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date))
        {
            value = DateOnly.FromDateTime(date);

            return true;
        }


        // -------------------------------------------------
        // Fallback Indian culture
        // -------------------------------------------------

        if (
            DateTime.TryParse(
                text,
                CultureInfo.GetCultureInfo("en-IN"),
                DateTimeStyles.None,
                out date))
        {
            value = DateOnly.FromDateTime(date);

            return true;
        }


        return false;
    }


    // =========================================================
    // NORMALIZE HEADER / TEXT
    // =========================================================

    private static string Normalize(string value)
    {
        return new string(
            value
                .Trim()
                .ToLowerInvariant()
                .Where(char.IsLetterOrDigit)
                .ToArray());
    }
}