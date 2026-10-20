using Makari.Api.Features.Catalog.Domain;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogDomainTests
{
    [Fact]
    public void Product_CanHaveMultipleVariantsWithTheirOwnPrices()
    {
        var product = new Product("Cold Brew", "Coffee");
        var small = product.AddVariant("Citrus", 250);
        var large = product.AddVariant("Citrus", 500);
        var local = small.AddPrice(CustomerType.Local, 40m);
        var visitor = small.AddPrice(CustomerType.Visitor, 50m);
        large.AddPrice(CustomerType.Local, 70m);

        Assert.True(product.IsActive);
        Assert.True(small.IsActive);
        Assert.Equal(2, product.Variants.Count);
        Assert.Equal(product.Id, small.ProductId);
        Assert.Same(product, small.Product);
        Assert.Equal(small.Id, local.ProductVariantId);
        Assert.Same(small, local.ProductVariant);
        Assert.Equal(2, small.Prices.Count);
        Assert.Equal(40m, local.Amount);
        Assert.Equal(CustomerType.Visitor, visitor.CustomerType);
        Guid[] ids = [product.Id, small.Id, large.Id, local.Id, visitor.Id];
        Assert.DoesNotContain(Guid.Empty, ids);
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Product_RejectsMissingNameOrCategory(string? value)
    {
        Assert.ThrowsAny<ArgumentException>(() => new Product(value!, "Coffee"));
        Assert.ThrowsAny<ArgumentException>(() => new Product("Cold Brew", value!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AddVariant_RejectsMissingName(string? name)
    {
        var product = new Product("Cold Brew", "Coffee");
        Assert.ThrowsAny<ArgumentException>(() => product.AddVariant(name!, 250));
        Assert.Empty(product.Variants);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void AddVariant_RejectsNonPositiveVolume(int sizeMl)
    {
        var product = new Product("Cold Brew", "Coffee");
        Assert.Throws<ArgumentOutOfRangeException>(() => product.AddVariant("Citrus", sizeMl));
        Assert.Empty(product.Variants);
    }

    public static TheoryData<decimal> InvalidAmounts => new()
    {
        0m, -0.01m, -100m, 0.001m, 12.345m, 10_000_000_000m, decimal.MaxValue
    };

    [Theory]
    [MemberData(nameof(InvalidAmounts))]
    public void AddPrice_RejectsInvalidAmount(decimal amount)
    {
        var variant = new Product("Cold Brew", "Coffee").AddVariant("Citrus", 250);
        Assert.Throws<ArgumentOutOfRangeException>(() => variant.AddPrice(CustomerType.Local, amount));
        Assert.Empty(variant.Prices);
    }

    public static TheoryData<decimal> ValidAmounts => new() { 0.01m, 40m, 40.120m, 9_999_999_999.99m };

    [Theory]
    [MemberData(nameof(ValidAmounts))]
    public void AddPrice_PreservesValidAmount(decimal amount)
    {
        var variant = new Product("Cold Brew", "Coffee").AddVariant("Citrus", 250);
        Assert.Equal(amount, variant.AddPrice(CustomerType.Local, amount).Amount);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void AddPrice_RejectsUndefinedCustomerType(int customerType)
    {
        var variant = new Product("Cold Brew", "Coffee").AddVariant("Citrus", 250);
        Assert.Throws<ArgumentOutOfRangeException>(() => variant.AddPrice((CustomerType)customerType, 40m));
        Assert.Empty(variant.Prices);
    }

    [Theory]
    [InlineData(CustomerType.Local)]
    [InlineData(CustomerType.Visitor)]
    public void AddPrice_RejectsDuplicateWithoutReplacingExistingPrice(CustomerType customerType)
    {
        var variant = new Product("Cold Brew", "Coffee").AddVariant("Citrus", 250);
        var original = variant.AddPrice(customerType, 40m);
        Assert.Throws<InvalidOperationException>(() => variant.AddPrice(customerType, 50m));
        Assert.Same(original, Assert.Single(variant.Prices));
        Assert.Equal(40m, original.Amount);
    }

    [Fact]
    public void Deactivate_PreservesVariantsAndPricesAndIsIdempotent()
    {
        var product = new Product("Cold Brew", "Coffee");
        var variant = product.AddVariant("Citrus", 250);
        var price = variant.AddPrice(CustomerType.Local, 40m);

        product.Deactivate();
        product.Deactivate();
        Assert.False(product.IsActive);
        Assert.True(variant.IsActive);
        Assert.Same(variant, Assert.Single(product.Variants));

        variant.Deactivate();
        variant.Deactivate();
        Assert.False(variant.IsActive);
        Assert.Same(price, Assert.Single(variant.Prices));
        Assert.Equal(40m, price.Amount);
    }

    [Fact]
    public void NavigationCollections_CannotBeMutatedExternally()
    {
        var product = new Product("Cold Brew", "Coffee");
        var variant = product.AddVariant("Citrus", 250);
        variant.AddPrice(CustomerType.Local, 40m);

        Assert.Throws<NotSupportedException>(() => ((ICollection<ProductVariant>)product.Variants).Clear());
        Assert.Throws<NotSupportedException>(() => ((ICollection<ProductPrice>)variant.Prices).Clear());
    }
}
