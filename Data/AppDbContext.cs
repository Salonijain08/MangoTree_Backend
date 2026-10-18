using Microsoft.EntityFrameworkCore;
using StockPortfolio.Api.Models;

namespace StockPortfolio.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<StockHolding> StockHoldings => Set<StockHolding>();
    public DbSet<StockSale> StockSales => Set<StockSale>();
    public DbSet<StockPurchase> StockPurchases => Set<StockPurchase>();
    public DbSet<StockClosingPrice> StockClosingPrices => Set<StockClosingPrice>();
    public DbSet<NseBhavcopyImport> NseBhavcopyImports => Set<NseBhavcopyImport>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<User>().HasIndex(x => x.Email).IsUnique();

        builder.Entity<StockHolding>().Property(x => x.BuyingQuantity).HasPrecision(18, 4);
        builder.Entity<StockHolding>().Property(x => x.BuyingPrice).HasPrecision(18, 4);
        builder.Entity<StockHolding>().Property(x => x.LatestClosingPrice).HasPrecision(18, 4);
        builder.Entity<StockHolding>().Property(x => x.LatestTradePrice).HasPrecision(18, 4);

        builder.Entity<StockPurchase>().Property(x => x.BuyingQuantity).HasPrecision(18, 4);
        builder.Entity<StockPurchase>().Property(x => x.BuyingPrice).HasPrecision(18, 4);
        builder.Entity<StockPurchase>()
            .HasIndex(x => new { x.UserId, x.SerialNo })
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

        builder.Entity<StockClosingPrice>().HasKey(x => new { x.Exchange, x.Series, x.PriceDate, x.Symbol });
        builder.Entity<StockClosingPrice>().Property(x => x.ClosingPrice).HasPrecision(18, 4);
        builder.Entity<NseBhavcopyImport>().HasKey(x => x.PriceDate);

        builder.Entity<StockSale>().Property(x => x.SellingQuantity).HasPrecision(18, 4);
        builder.Entity<StockSale>().Property(x => x.SellingPrice).HasPrecision(18, 4);
        builder.Entity<StockSale>().Property(x => x.BuyPriceAtSale).HasPrecision(18, 4);

        builder.Entity<StockHolding>().HasOne(x => x.User).WithMany(x => x.Holdings)
            .HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);

        builder.Entity<StockSale>().HasOne(x => x.StockHolding).WithMany(x => x.Sales)
            .HasForeignKey(x => x.StockHoldingId).OnDelete(DeleteBehavior.Cascade);
    }
}
