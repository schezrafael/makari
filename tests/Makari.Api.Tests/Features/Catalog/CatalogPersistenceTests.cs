using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogPersistenceTests
{
    private static MakariDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_model_tests")
            .Options);

    [Fact]
    public void Model_HasRequiredRestrictedRelationshipsAndUniquePriceIndex()
    {
        using var context = CreateContext();
        var variant = context.Model.FindEntityType(typeof(ProductVariant))!;
        var price = context.Model.FindEntityType(typeof(ProductPrice))!;

        var productForeignKey = Assert.Single(variant.GetForeignKeys());
        Assert.True(productForeignKey.IsRequired);
        Assert.Equal(typeof(Product), productForeignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, productForeignKey.DeleteBehavior);
        var variantForeignKey = Assert.Single(price.GetForeignKeys());
        Assert.True(variantForeignKey.IsRequired);
        Assert.Equal(typeof(ProductVariant), variantForeignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, variantForeignKey.DeleteBehavior);

        var index = Assert.Single(price.GetIndexes(), index => index.IsUnique);
        Assert.Equal(new[] { "ProductVariantId", "CustomerType" }, index.Properties.Select(p => p.Name));
        var amount = price.FindProperty(nameof(ProductPrice.Amount))!;
        Assert.Equal(12, amount.GetPrecision());
        Assert.Equal(2, amount.GetScale());
    }

    [Fact]
    public void GeneratedPostgresSchema_ContainsConstraintsWithoutConnectingToDatabase()
    {
        using var context = CreateContext();
        var sql = context.Database.GenerateCreateScript();

        Assert.Contains("\"Amount\" numeric(12,2) NOT NULL", sql);
        Assert.Contains("CHECK (\"Amount\" > 0)", sql);
        Assert.Contains("CHECK (\"SizeMl\" > 0)", sql);
        Assert.Contains("CHECK (\"CustomerType\" IN (0, 1))", sql);
        Assert.Contains("CREATE UNIQUE INDEX", sql);
        Assert.Contains("(\"ProductVariantId\", \"CustomerType\")", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
    }

    [Fact]
    public void TrackingGraphAndDeactivating_DoesNotDeleteHistory()
    {
        using var context = CreateContext();
        var product = new Product("Cold Brew", "Coffee");
        var variant = product.AddVariant("Citrus", 250);
        var price = variant.AddPrice(CustomerType.Local, 40m);
        context.Products.Add(product);
        Assert.Equal(3, context.ChangeTracker.Entries().Count());
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Added, entry.State));
        context.ChangeTracker.AcceptAllChanges();

        product.Deactivate();
        variant.Deactivate();
        context.ChangeTracker.DetectChanges();

        Assert.Equal(EntityState.Modified, context.Entry(product).State);
        Assert.Equal(EntityState.Modified, context.Entry(variant).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(price).State);
        Assert.All(context.ChangeTracker.Entries().Where(entry => entry.State == EntityState.Modified),
            entry => Assert.Equal("IsActive", Assert.Single(entry.Properties, p => p.IsModified).Metadata.Name));
    }
}
