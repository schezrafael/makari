using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.CashCloses.Domain;
using Makari.Api.Features.Sales.Domain;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Infrastructure.Persistence;

public sealed class MakariDbContext(DbContextOptions<MakariDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<DailyCashClose> DailyCashCloses => Set<DailyCashClose>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MakariDbContext).Assembly);
    }
}
