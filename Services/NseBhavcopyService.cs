using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.VisualBasic.FileIO;
using StockPortfolio.Api.Data;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Services;

public sealed class NseBhavcopyService(HttpClient http, AppDbContext db, IMemoryCache cache)
{
    private static readonly SemaphoreSlim[] Locks = Enumerable.Range(0, 32)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public async Task<IReadOnlyDictionary<string, StockClosingPrice>> GetDateAsync(DateOnly date, CancellationToken ct)
    {
        if (date < MarketClock.FirstUdIfDate || date > MarketClock.Today)
            throw new MarketDataException("INVALID_DATE", "NSE UDiFF dates must be between 2024-07-08 and today (IST).");
        var gate = Locks[date.DayNumber % Locks.Length];
        await gate.WaitAsync(ct);
        try
        {
            if (await db.NseBhavcopyImports.AsNoTracking().AnyAsync(x => x.PriceDate == date, ct))
                return await ReadCacheAsync(date, ct);
            var errorKey = "nse-error:" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            if (cache.TryGetValue<MarketDataException>(errorKey, out var recentError) && recentError is not null)
                throw recentError;

            List<StockClosingPrice> rows;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                var file = $"BhavCopy_NSE_CM_0_0_0_{date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_F_0000.csv.zip";
                using var response = await http.GetAsync("/content/cm/" + file,
                    HttpCompletionOption.ResponseHeadersRead, timeout.Token);
                if (response.StatusCode == HttpStatusCode.NotFound)
                    throw new MarketDataException("NSE_NOT_AVAILABLE", $"NSE final Bhavcopy is not available for {date:yyyy-MM-dd}.");
                if (!response.IsSuccessStatusCode)
                    throw new MarketDataException("NSE_HTTP_ERROR", $"NSE download returned HTTP {(int)response.StatusCode}. This does not establish a market holiday.");
                var bytes = await MarketHttp.ReadLimitedAsync(response.Content, 12_000_000, timeout.Token);
                rows = ParseZip(bytes, date, DateTimeOffset.UtcNow, ct);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                var error = new MarketDataException("NSE_TIMEOUT", "NSE download timed out. Try again later.");
                cache.Set(errorKey, error, TimeSpan.FromSeconds(30));
                throw error;
            }
            catch (HttpRequestException)
            {
                var error = new MarketDataException("NSE_UNAVAILABLE", "NSE could not be reached. Try again later.");
                cache.Set(errorKey, error, TimeSpan.FromSeconds(30));
                throw error;
            }
            catch (MarketDataException ex)
            {
                cache.Set(errorKey, ex, TimeSpan.FromSeconds(30));
                throw;
            }

            var import = new NseBhavcopyImport
            {
                PriceDate = date, RowCount = rows.Count, FetchedAtUtc = DateTimeOffset.UtcNow
            };
            db.StockClosingPrices.AddRange(rows);
            db.NseBhavcopyImports.Add(import);
            try
            {
                // EF saves the daily rows + marker atomically in one transaction.
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Another server instance may have imported the same date first.
                foreach (var row in rows) db.Entry(row).State = EntityState.Detached;
                db.Entry(import).State = EntityState.Detached;
                if (!await db.NseBhavcopyImports.AsNoTracking().AnyAsync(x => x.PriceDate == date, ct)) throw;
                return await ReadCacheAsync(date, ct);
            }
            foreach (var row in rows) db.Entry(row).State = EntityState.Detached;
            db.Entry(import).State = EntityState.Detached;
            return rows.ToDictionary(x => x.Symbol, StringComparer.OrdinalIgnoreCase);
        }
        finally { gate.Release(); }
    }

    private async Task<IReadOnlyDictionary<string, StockClosingPrice>> ReadCacheAsync(DateOnly date, CancellationToken ct) =>
        await db.StockClosingPrices.AsNoTracking()
            .Where(x => x.PriceDate == date && x.Exchange == "NSE" && x.Series == "EQ")
            .ToDictionaryAsync(x => x.Symbol, StringComparer.OrdinalIgnoreCase, ct);

    public static List<StockClosingPrice> ParseZip(byte[] zip, DateOnly requestedDate,
        DateTimeOffset fetchedAt, CancellationToken ct = default)
    {
        try
        {
            using var memory = new MemoryStream(zip);
            using var archive = new ZipArchive(memory, ZipArchiveMode.Read);
            var expected = $"BhavCopy_NSE_CM_0_0_0_{requestedDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}_F_0000.csv";
            var entries = archive.Entries.Where(x => x.Name.Equals(expected, StringComparison.OrdinalIgnoreCase)).ToList();
            if (entries.Count != 1 || entries[0].Length > 60_000_000)
                throw new MarketDataException("NSE_INVALID_ZIP", "NSE ZIP does not contain the expected daily CSV.");
            using var stream = entries[0].Open();
            using var parser = new TextFieldParser(stream, Encoding.UTF8)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = true
            };
            parser.SetDelimiters(",");
            var headers = parser.ReadFields() ?? throw new MarketDataException("NSE_INVALID_CSV", "NSE CSV has no header.");
            int Column(string name)
            {
                var index = Array.FindIndex(headers, h => h.Trim('\uFEFF', ' ').Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw new MarketDataException("NSE_INVALID_CSV", $"NSE CSV column {name} is missing.");
                return index;
            }
            int dateCol = Column("TradDt"), symbolCol = Column("TckrSymb"),
                seriesCol = Column("SctySrs"), closeCol = Column("ClsPric"),
                sourceCol = Column("Src"), segmentCol = Column("Sgmt");
            int max = new[] { dateCol, symbolCol, seriesCol, closeCol, sourceCol, segmentCol }.Max();
            var result = new Dictionary<string, StockClosingPrice>(StringComparer.OrdinalIgnoreCase);
            while (!parser.EndOfData)
            {
                ct.ThrowIfCancellationRequested();
                var fields = parser.ReadFields();
                if (fields is null || fields.All(string.IsNullOrWhiteSpace)) continue;
                if (fields.Length <= max ||
                    !DateOnly.TryParseExact(fields[dateCol], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date) ||
                    date != requestedDate)
                    throw new MarketDataException("NSE_DATE_MISMATCH", "NSE CSV has malformed rows or a different trading date.");
                if (fields[sourceCol] != "NSE" || fields[segmentCol] != "CM")
                    throw new MarketDataException("NSE_INVALID_CSV", "The file is not an NSE cash-market report.");
                // The current holding model has no Series field. Support EQ explicitly only.
                if (fields[seriesCol] != "EQ") continue;
                var symbol = fields[symbolCol].Trim().ToUpperInvariant();
                if (symbol.Length is 0 or > 30 ||
                    !decimal.TryParse(fields[closeCol], NumberStyles.Number, CultureInfo.InvariantCulture, out var close) || close <= 0)
                    throw new MarketDataException("NSE_INVALID_CSV", "An EQ row has an invalid closing price or symbol.");
                if (!result.TryAdd(symbol, new StockClosingPrice
                    {
                        Symbol = symbol, Exchange = "NSE", Series = "EQ", PriceDate = date,
                        ClosingPrice = decimal.Round(close, 4), FetchedAtUtc = fetchedAt
                    }))
                    throw new MarketDataException("NSE_DUPLICATE_SYMBOL", $"NSE file has duplicate EQ rows for {symbol}.");
            }
            if (result.Count == 0)
                throw new MarketDataException("NSE_NO_EQ_DATA", "No EQ closing prices were found in the NSE file.");
            return result.Values.ToList();
        }
        catch (Exception ex) when (ex is InvalidDataException or MalformedLineException or IOException)
        {
            throw new MarketDataException("NSE_INVALID_FILE", "NSE returned an unreadable ZIP/CSV file.");
        }
    }
}
