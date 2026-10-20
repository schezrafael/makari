using Makari.Api.Features.Catalog.Domain;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Infrastructure.Persistence;

public sealed class MakariDbContext(DbContextOptions<MakariDbContext> options)
    : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductPrice> ProductPrices => Set<ProductPrice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(MakariDbContext).Assembly);
    }
}
