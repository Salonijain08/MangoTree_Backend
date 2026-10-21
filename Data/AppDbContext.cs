using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<StockHolding> StockHoldings => Set<StockHolding>();

    public DbSet<StockSale> StockSales => Set<StockSale>();

    public DbSet<StockPurchase> StockPurchases => Set<StockPurchase>();

    public DbSet<StockSaleAllocation> StockSaleAllocations =>
        Set<StockSaleAllocation>();

    public DbSet<StockClosingPrice> StockClosingPrices =>
        Set<StockClosingPrice>();

    public DbSet<NseBhavcopyImport> NseBhavcopyImports =>
        Set<NseBhavcopyImport>();


    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);


        // =========================================================
        // USER
        // =========================================================

        builder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();


        // =========================================================
        // STOCK HOLDING
        // =========================================================

        builder.Entity<StockHolding>()
            .Property(x => x.BuyingQuantity)
            .HasPrecision(18, 4);

        builder.Entity<StockHolding>()
            .Property(x => x.BuyingPrice)
            .HasPrecision(18, 4);

        builder.Entity<StockHolding>()
            .Property(x => x.LatestClosingPrice)
            .HasPrecision(18, 4);

        builder.Entity<StockHolding>()
            .Property(x => x.LatestTradePrice)
            .HasPrecision(18, 4);


        // =========================================================
        // STOCK PURCHASE
        // =========================================================

        builder.Entity<StockPurchase>()
            .Property(x => x.BuyingQuantity)
            .HasPrecision(18, 4);

        builder.Entity<StockPurchase>()
            .Property(x => x.BuyingPrice)
            .HasPrecision(18, 4);

        builder.Entity<StockPurchase>()
            .HasIndex(x => new
            {
                x.UserId,
                x.SerialNo
            })
            .IsUnique();

        builder.Entity<StockPurchase>()
            .HasOne(x => x.StockHolding)
            .WithMany(x => x.Purchases)
            .HasForeignKey(x => x.StockHoldingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockPurchase>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.NoAction);


        // =========================================================
        // STOCK SALE
        // =========================================================

        builder.Entity<StockSale>()
            .Property(x => x.SellingQuantity)
            .HasPrecision(18, 4);

        builder.Entity<StockSale>()
            .Property(x => x.SellingPrice)
            .HasPrecision(18, 4);

        builder.Entity<StockSale>()
            .Property(x => x.BuyPriceAtSale)
            .HasPrecision(18, 4);


        // =========================================================
        // STOCK SALE FIFO ALLOCATION
        // =========================================================

        builder.Entity<StockSaleAllocation>()
            .Property(x => x.Quantity)
            .HasPrecision(18, 4);

        builder.Entity<StockSaleAllocation>()
            .Property(x => x.BuyingPrice)
            .HasPrecision(18, 4);

        builder.Entity<StockSaleAllocation>()
            .Property(x => x.BuyValue)
            .HasPrecision(18, 4);

        builder.Entity<StockSaleAllocation>()
            .Property(x => x.Term)
            .HasMaxLength(20);

        builder.Entity<StockSaleAllocation>()
            .HasOne(x => x.StockSale)
            .WithMany(x => x.Allocations)
            .HasForeignKey(x => x.StockSaleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockSaleAllocation>()
            .HasOne(x => x.StockPurchase)
            .WithMany(x => x.SaleAllocations)
            .HasForeignKey(x => x.StockPurchaseId)
            .OnDelete(DeleteBehavior.Restrict);


        // =========================================================
        // STOCK CLOSING PRICE
        // =========================================================

        builder.Entity<StockClosingPrice>()
            .HasKey(x => new
            {
                x.Exchange,
                x.Series,
                x.PriceDate,
                x.Symbol
            });

        builder.Entity<StockClosingPrice>()
            .Property(x => x.ClosingPrice)
            .HasPrecision(18, 4);


        // =========================================================
        // NSE BHAVCOPY IMPORT
        // =========================================================

        builder.Entity<NseBhavcopyImport>()
            .HasKey(x => x.PriceDate);


        // =========================================================
        // HOLDING RELATIONSHIPS
        // =========================================================

        builder.Entity<StockHolding>()
            .HasOne(x => x.User)
            .WithMany(x => x.Holdings)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockSale>()
            .HasOne(x => x.StockHolding)
            .WithMany(x => x.Sales)
            .HasForeignKey(x => x.StockHoldingId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}