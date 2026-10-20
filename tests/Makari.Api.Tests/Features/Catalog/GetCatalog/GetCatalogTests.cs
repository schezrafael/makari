using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Catalog.GetCatalog;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Catalog.GetCatalog;

public sealed class GetCatalogTests
{
    [Theory]
    [InlineData("Visitor", 60, 75)]
    [InlineData("Local", 50, 60)]
    public async Task GetCatalog_ReturnsOnlyRequestedPrices(string customerType, int naturalPrice, int citricPrice)
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Cold Brew", "Café");
        var natural = product.AddVariant("Natural", 500);
        natural.AddPrice(CustomerType.Visitor, 60m);
        natural.AddPrice(CustomerType.Local, 50m);
        var citric = product.AddVariant("Cítrico", 500);
        citric.AddPrice(CustomerType.Visitor, 75m);
        citric.AddPrice(CustomerType.Local, 60m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        using var response = await client.GetAsync($"/api/catalog?customerType={customerType}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<GetCatalogResponse>();
        var result = Assert.Single(body!.Products);
        Assert.Equal(product.Id, result.Id);
        Assert.Equal("Cold Brew", result.Name);
        Assert.Equal("Café", result.Category);
        Assert.Equal(2, result.Variants.Count);
        Assert.Equal((decimal)naturalPrice, Assert.Single(result.Variants, v => v.Id == natural.Id).Price);
        Assert.Equal((decimal)citricPrice, Assert.Single(result.Variants, v => v.Id == citric.Id).Price);
        Assert.All(result.Variants, variant => Assert.Equal(500, variant.SizeMl));

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var productJson = json.RootElement.GetProperty("products")[0];
        Assert.Equal(new[] { "category", "id", "name", "variants" },
            productJson.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(new[] { "id", "name", "price", "sizeMl" },
            productJson.GetProperty("variants")[0].EnumerateObject().Select(p => p.Name).Order());
    }

    [Theory]
    [InlineData("")]
    [InlineData("?customerType=")]
    [InlineData("?customerType=Unknown")]
    [InlineData("?customerType=0")]
    [InlineData("?customerType=1")]
    [InlineData("?customerType=99")]
    [InlineData("?customerType=visitor")]
    [InlineData("?customerType=Local%2CVisitor")]
    [InlineData("?customerType=Local&customerType=Visitor")]
    public async Task GetCatalog_RejectsInvalidCustomerTypeBeforeQuerying(string query)
    {
        using var factory = new CatalogApiFactory();
        // Deliberately do not create any tables: validation must happen before querying.
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/catalog" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("customerType", out _));
    }

    [Fact]
    public async Task GetCatalog_ExcludesInactiveProducts()
    {
        using var factory = new CatalogApiFactory();
        var active = ProductWithPrice("Active");
        var inactive = ProductWithPrice("Inactive");
        inactive.Deactivate();
        await factory.InitializeAsync(active, inactive);
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<GetCatalogResponse>("/api/catalog?customerType=Visitor");

        Assert.Equal(active.Id, Assert.Single(body!.Products).Id);
    }

    [Fact]
    public async Task GetCatalog_ExcludesInactiveVariants()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrice("Cold Brew");
        var inactive = product.AddVariant("Inactive", 500);
        inactive.AddPrice(CustomerType.Visitor, 70m);
        inactive.Deactivate();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<GetCatalogResponse>("/api/catalog?customerType=Visitor");

        Assert.Equal("Natural", Assert.Single(Assert.Single(body!.Products).Variants).Name);
    }

    [Theory]
    [InlineData(CustomerType.Visitor, CustomerType.Local)]
    [InlineData(CustomerType.Local, CustomerType.Visitor)]
    public async Task GetCatalog_ExcludesVariantsWithoutApplicablePrice(
        CustomerType requested, CustomerType available)
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Cold Brew", "Café");
        product.AddVariant("Other customer type", 500).AddPrice(available, 50m);
        product.AddVariant("No prices", 500);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<GetCatalogResponse>($"/api/catalog?customerType={requested}");

        Assert.Empty(Assert.Single(body!.Products).Variants);
    }

    [Fact]
    public async Task GetCatalog_EmptyCatalogReturnsEmptyCollection()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/api/catalog?customerType=Visitor");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("{\"products\":[]}", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task GetCatalog_OrdersProductsAndVariantsByName()
    {
        using var factory = new CatalogApiFactory();
        var last = ProductWithPrice("Zebra");
        var first = new Product("Alpha", "Café");
        first.AddVariant("Zebra", 500).AddPrice(CustomerType.Visitor, 60m);
        first.AddVariant("Alpha", 500).AddPrice(CustomerType.Visitor, 50m);
        await factory.InitializeAsync(last, first);
        using var client = factory.CreateClient();

        var body = await client.GetFromJsonAsync<GetCatalogResponse>("/api/catalog?customerType=Visitor");

        Assert.Equal(new[] { "Alpha", "Zebra" }, body!.Products.Select(product => product.Name));
        Assert.Equal(new[] { "Alpha", "Zebra" }, body.Products[0].Variants.Select(variant => variant.Name));
    }

    [Fact]
    public async Task Handler_DoesNotTrackQueriedEntities()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync(ProductWithPrice("Cold Brew"));
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<GetCatalogHandler>();

        var response = await handler.HandleAsync(CustomerType.Visitor);

        Assert.Single(response.Products);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    private static Product ProductWithPrice(string name)
    {
        var product = new Product(name, "Café");
        product.AddVariant("Natural", 500).AddPrice(CustomerType.Visitor, 60m);
        return product;
    }
}
