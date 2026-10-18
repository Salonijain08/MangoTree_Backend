# Stock Portfolio API (.NET 8 + SQL Server)

Backend for the stock portfolio panel. The Excel workflow now uses one **live/master Excel file**. Every buying transaction has a permanent `Serial No`, and syncing the same Excel again does not create duplicates.

## 1. Configure

Update `appsettings.json` with your SQL Server connection and JWT settings.

For production, keep secrets in environment variables instead of committing them:

```powershell
$env:ConnectionStrings__DefaultConnection="YOUR_SQL_CONNECTION"
$env:Jwt__Key="YOUR_RANDOM_SECRET_OF_AT_LEAST_32_CHARACTERS"
```

## 2. Database and run

Restore packages and apply the existing migrations:

```powershell
dotnet restore
dotnet tool install --global dotnet-ef
dotnet ef database update
dotnet run
```

The new migration is:

```text
20261006120000_AddPurchaseHistoryAndExcelSerial
```

It:
- creates `StockPurchases`;
- adds `SerialNo` to each purchase;
- adds a unique `(UserId, SerialNo)` index;
- preserves existing holdings as legacy purchase lots;
- stores the old sale cost basis in `StockSales.BuyPriceAtSale`.

Swagger opens at:

`https://localhost:7067/swagger`

## 3. Live Excel format

Maintain **one Excel file** and keep adding new buying rows to it.

Recommended columns:

| SERIAL NO | COMPANY | SYMBOL | EXCHANGE | DATE | BUY PRICE | QUANTITY | VOLUME |
|---:|---|---|---|---|---:|---:|---:|
| 1 | TCS | TCS | NSE | 01/10/2026 | 3200 | 5 | =F2*G2 |
| 2 | INFY | INFY | NSE | 02/10/2026 | 1500 | 10 | =F3*G3 |

`SERIAL NO` must be a positive unique number. Never reuse a serial number for a different purchase.

`EXCHANGE` defaults to `NSE` if blank.

`VOLUME` is optional and is only for Excel readability; the API calculates buy value from quantity × price.

## 4. Excel sync

Upload the live Excel to:

`POST /api/holdings/import-excel`

multipart/form-data field:

```text
file
```

Sync behavior:

```text
Live Excel
   ↓
Read every purchase row
   ↓
Serial No already in database?
   ├── YES → skip
   └── NO  → import
             ↓
      Same symbol + exchange?
        ├── YES → add purchase to existing holding
        └── NO  → create new holding
```

The response includes:

- `imported`: newly imported purchase rows
- `skipped`: rows whose Serial No already exists (or a matching legacy lot was adopted)
- `migratedLegacyRows`: first-sync legacy rows matched to an existing holding
- `newlyCreatedHoldings`
- `totalRowsRead`

So, for example:

```text
First sync:
3 rows → imported = 3

Second sync, same Excel:
3 rows → imported = 0, skipped = 3

Add Serial No 4:
4 rows → imported = 1, skipped = 3
```

## 5. Same-stock buying history

Multiple purchases of the same symbol/exchange are stored as separate `StockPurchase` records but displayed as one holding.

Example:

```text
Serial 11 → ATHERENERG → 5 @ ₹1,500
Serial 23 → ATHERENERG → 10 @ ₹1,700
```

The holding becomes:

```text
Total purchased quantity = 15
Average buying price     = ₹1,633.3333
```

The API response now includes:

```json
"purchases": [
  {
    "id": 11,
    "serialNo": 11,
    "buyingDate": "2026-10-05",
    "buyingQuantity": 5,
    "buyingPrice": 1500,
    "buyValue": 7500
  },
  {
    "id": 23,
    "serialNo": 23,
    "buyingDate": "2026-10-10",
    "buyingQuantity": 10,
    "buyingPrice": 1700,
    "buyValue": 17000
  }
]
```

The existing `BuyingPrice` field is now the **weighted average buying price**, so existing frontend code can continue reading that field.

## 6. P/L behavior

For an active holding:

```text
Average Buying Price = Total purchase cost / Total purchased quantity

Current Value = Remaining quantity × Latest Trade/Close Price

Unrealized P/L =
    (Latest Price − Average Buying Price) × Remaining quantity
```

The API continues returning:

- `BuyingQuantity`
- `BuyingPrice` (weighted average)
- `SoldQuantity`
- `RemainingQuantity`
- `Investment`
- `RemainingCost`
- `CurrentValue`
- `RealizedProfitLoss`
- `UnrealizedProfitLoss`
- `TotalProfitLoss`
- `ProfitLossPercentage`
- `Purchases`
- `Sales`

## 7. Sale cost basis

When a sale is created, the current weighted average buying price is stored in `BuyPriceAtSale`.

This prevents a future purchase from changing the historical P/L of an older sale.

Example:

```text
Before sale:
5 @ ₹1,500
10 @ ₹1,700
Average = ₹1,633.3333

Sale:
6 @ ₹1,800

Sale cost basis:
₹1,633.3333 × 6
```

If another purchase is added later, the old sale's realized P/L remains unchanged.

Existing sales are migrated with their old `StockHolding.BuyingPrice` as their cost basis.

## 8. Holding APIs

- `GET /api/holdings`
- `GET /api/holdings/{id}`
- `POST /api/holdings`
- `PUT /api/holdings/{id}`
- `DELETE /api/holdings/{id}`
- `POST /api/holdings/import-excel`
- `POST /api/holdings/{id}/sync-close`
- `POST /api/holdings/sync-all-closes`

`POST /api/holdings` also creates a purchase transaction with the next Serial No.

For a holding that contains multiple purchase transactions, `PUT /api/holdings/{id}` intentionally does not overwrite the aggregate purchase history. New purchases should be added through the live Excel sync.

## 9. Sales

- `POST /api/holdings/{holdingId}/sales`

Example:

```json
{
  "sellingDate": "2026-10-05",
  "sellingQuantity": 6,
  "sellingPrice": 1800
}
```

The API prevents total sold quantity from exceeding total purchased quantity.

## 10. First live-Excel sync after upgrading

The migration preserves the existing database.

Existing holdings are temporarily represented by legacy purchase records with negative Serial Nos. The first live Excel sync can automatically adopt one of these legacy records when the Excel row exactly matches:

- symbol
- exchange
- buying date
- buying quantity
- buying price

When it matches, the legacy negative Serial No is replaced by the Serial No from your live Excel.

For the cleanest first sync, make each existing holding's current purchase information one row in the new live Excel. After that, only append new purchase rows with new Serial Nos.

## 11. Important

Do not delete or reuse Serial Nos.

For example:

```text
11 = ATHERENERG purchase on 05 Oct 2026
23 = ATHERENERG purchase on 10 Oct 2026
```

If the same Excel is synced tomorrow, both are skipped. If you add:

```text
31 = ATHERENERG purchase on 20 Oct 2026
```

only Serial No 31 is imported.
