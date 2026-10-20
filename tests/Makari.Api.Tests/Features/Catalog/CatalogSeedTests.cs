using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Catalog.Persistence;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogSeedTests
{
    private static MakariDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_seed_unit_tests").Options);

    [Fact]
    public void EmptyCatalog_GetsOnlyTheRequestedProductVariantsAndPrices()
    {
        using var context = CreateContext();
        CatalogSeeder.AddMissingEntries(context, []);

        var product = Assert.Single(context.Products.Local);
        Assert.Equal(CatalogSeeder.ColdBrewId, product.Id);
        Assert.Equal("Cold Brew", product.Name);
        Assert.Equal("Café", product.Category);
        Assert.True(product.IsActive);
        Assert.Equal(2, product.Variants.Count);
        var natural = Assert.Single(product.Variants, variant => variant.Name == "Natural");
        var citric = Assert.Single(product.Variants, variant => variant.Name == "Cítrico");
        Assert.Equal(CatalogSeeder.NaturalId, natural.Id);
        Assert.Equal(CatalogSeeder.CitricId, citric.Id);
        Assert.All(product.Variants, variant =>
        {
            Assert.Equal(500, variant.SizeMl);
            Assert.True(variant.IsActive);
            Assert.Equal(product.Id, variant.ProductId);
            Assert.Equal(2, variant.Prices.Count);
            Assert.All(variant.Prices, price => Assert.Equal(variant.Id, price.ProductVariantId));
        });
        AssertPrice(natural, CustomerType.Visitor, 60m);
        AssertPrice(natural, CustomerType.Local, 50m);
        AssertPrice(citric, CustomerType.Visitor, 75m);
        AssertPrice(citric, CustomerType.Local, 60m);
        Assert.Equal(7, context.ChangeTracker.Entries().Count());
    }

    [Fact]
    public void FreshRuns_UseTheSameSevenIdentifiers()
    {
        using var first = CreateContext();
        using var second = CreateContext();
        CatalogSeeder.AddMissingEntries(first, []);
        CatalogSeeder.AddMissingEntries(second, []);
        var firstIds = first.ChangeTracker.Entries().Select(e => (Guid)e.Property("Id").CurrentValue!).Order().ToArray();
        var secondIds = second.ChangeTracker.Entries().Select(e => (Guid)e.Property("Id").CurrentValue!).Order().ToArray();
        Assert.Equal(firstIds, secondIds);
        Assert.Equal(7, firstIds.Distinct().Count());
    }

    [Fact]
    public void RepeatedRun_DoesNotInsertOrUpdateAnything()
    {
        using var context = CreateContext();
        CatalogSeeder.AddMissingEntries(context, []);
        context.ChangeTracker.AcceptAllChanges();

        CatalogSeeder.AddMissingEntries(context, context.Products.Local.ToArray());
        context.ChangeTracker.DetectChanges();

        Assert.Equal(7, context.ChangeTracker.Entries().Count());
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public void ExistingNaturalKeys_AreReusedAndOperatorPriceAndInactiveStatesArePreserved()
    {
        using var context = CreateContext();
        var product = new Product("cold brew", "Operator category");
        var natural = product.AddVariant("natural", 500);
        var price = natural.AddPrice(CustomerType.Local, 99m);
        product.Deactivate();
        natural.Deactivate();
        context.Products.Attach(product);

        CatalogSeeder.AddMissingEntries(context, [product]);
        context.ChangeTracker.DetectChanges();

        Assert.Single(context.Products.Local);
        Assert.Equal(2, product.Variants.Count);
        Assert.Same(natural, product.Variants.First());
        Assert.Equal(99m, price.Amount);
        Assert.Equal("Operator category", product.Category);
        Assert.False(product.IsActive);
        Assert.False(natural.IsActive);
        Assert.Equal(EntityState.Unchanged, context.Entry(product).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(natural).State);
        Assert.Equal(EntityState.Unchanged, context.Entry(price).State);
        Assert.Equal(4, context.ChangeTracker.Entries().Count(e => e.State == EntityState.Added));
    }

    [Fact]
    public void StableIds_PreserveRenamedEntriesAndChangedPrices()
    {
        using var context = CreateContext();
        CatalogSeeder.AddMissingEntries(context, []);
        var product = Assert.Single(context.Products.Local);
        var natural = Assert.Single(product.Variants, variant => variant.Id == CatalogSeeder.NaturalId);
        var price = Assert.Single(natural.Prices, price => price.CustomerType == CustomerType.Local);
        context.Entry(product).Property(p => p.Name).CurrentValue = "Operator Cold Brew";
        context.Entry(natural).Property(v => v.Name).CurrentValue = "Operator Natural";
        context.Entry(price).Property(p => p.Amount).CurrentValue = 88m;
        context.ChangeTracker.AcceptAllChanges();

        CatalogSeeder.AddMissingEntries(context, [product]);
        context.ChangeTracker.DetectChanges();

        Assert.Equal("Operator Cold Brew", product.Name);
        Assert.Equal("Operator Natural", natural.Name);
        Assert.Equal(88m, price.Amount);
        Assert.All(context.ChangeTracker.Entries(), entry => Assert.Equal(EntityState.Unchanged, entry.State));
    }

    [Fact]
    public void AmbiguousExistingProducts_RequireManualReview()
    {
        using var context = CreateContext();
        var products = new[] { new Product("Cold Brew", "Café"), new Product("cold brew", "Coffee") };
        Assert.Throws<InvalidOperationException>(() => CatalogSeeder.AddMissingEntries(context, products));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public void AmbiguousExistingVariants_RequireManualReview()
    {
        using var context = CreateContext();
        var product = new Product("Cold Brew", "Café");
        product.AddVariant("Natural", 500);
        product.AddVariant("natural", 500);
        context.Products.Attach(product);
        Assert.Throws<InvalidOperationException>(() => CatalogSeeder.AddMissingEntries(context, [product]));
        Assert.DoesNotContain(context.ChangeTracker.Entries(), e => e.State == EntityState.Added);
    }

    [Fact]
    public void StableVariantIdOwnedByAnotherProduct_IsRejected()
    {
        using var context = CreateContext();
        var other = new Product("Other product", "Other category");
        var variant = other.AddVariant("Other variant", 500);
        context.Entry(variant).Property(v => v.Id).CurrentValue = CatalogSeeder.NaturalId;
        context.Products.Attach(other);
        Assert.Throws<InvalidOperationException>(() => CatalogSeeder.AddMissingEntries(context, [other]));
    }

    private static void AssertPrice(ProductVariant variant, CustomerType customerType, decimal amount) =>
        Assert.Equal(amount, Assert.Single(variant.Prices, price => price.CustomerType == customerType).Amount);
}
