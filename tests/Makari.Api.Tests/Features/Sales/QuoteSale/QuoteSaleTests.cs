using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Sales.QuoteSale;

public sealed class QuoteSaleTests
{
    [Theory]
    [InlineData("Visitor", 0, 195)]
    [InlineData("Local", 35, 160)]
    public async Task Quote_UsesCurrentPricesWithoutWriting_AndMatchesIdempotentSale(string customerType, decimal discount, decimal total)
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
        var items = new[] { new { productVariantId = natural.Id, quantity = 2 }, new { productVariantId = citric.Id, quantity = 1 } };

        using var quote = await client.PostAsJsonAsync("/api/sales/quote", new { customerType, items });
        Assert.Equal(HttpStatusCode.OK, quote.StatusCode);
        using var json = JsonDocument.Parse(await quote.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(customerType, body.GetProperty("customerType").GetString());
        Assert.Equal(195m, body.GetProperty("subtotal").GetDecimal());
        Assert.Equal(discount, body.GetProperty("discount").GetDecimal());
        Assert.Equal(total, body.GetProperty("total").GetDecimal());
        Assert.Equal(2, body.GetProperty("items").GetArrayLength());
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(0, await context.Sales.CountAsync());
        Assert.Equal(0, await context.SaleItems.CountAsync());
        Assert.Equal(4, await context.ProductPrices.CountAsync());
        Assert.Empty(context.ChangeTracker.Entries());

        var saleRequest = new { customerType, items, paymentMethod = "Cash", idempotencyKey = Guid.NewGuid() };
        using var sale = await client.PostAsJsonAsync("/api/sales", saleRequest);
        using var replay = await client.PostAsJsonAsync("/api/sales", saleRequest);
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var saleJson = JsonDocument.Parse(await sale.Content.ReadAsStringAsync());
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        Assert.Equal(total, saleJson.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(saleJson.RootElement.GetProperty("id").GetGuid(), replayJson.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(1, await context.Sales.CountAsync());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"customerType\":\"Unknown\",\"items\":[]}")]
    [InlineData("{\"customerType\":1,\"items\":[]}")]
    [InlineData("{\"customerType\":\"Local\",\"items\":[]}")]
    [InlineData("{\"customerType\":\"Local\",\"items\":null}")]
    [InlineData("{\"customerType\":\"Local\",\"items\":[null]}")]
    [InlineData("{\"customerType\":\"Local\",\"items\":[{\"productVariantId\":\"00000000-0000-0000-0000-000000000000\",\"quantity\":1}]}")]
    public async Task Quote_RejectsInvalidRequestBeforeDatabaseQuery(string body)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/sales/quote", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Quote_RejectsNonPositiveQuantities(int quantity)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales/quote", new { customerType = "Local", items = new[] { new { productVariantId = Guid.NewGuid(), quantity } } });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_RejectsDuplicatesAndClientAmounts()
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        var item = new { productVariantId = Guid.NewGuid(), quantity = 1 };
        using var duplicate = await client.PostAsJsonAsync("/api/sales/quote", new { customerType = "Local", items = new[] { item, item } });
        using var total = await client.PostAsJsonAsync("/api/sales/quote", new { customerType = "Local", total = 1, items = new[] { item } });
        using var price = await client.PostAsJsonAsync("/api/sales/quote", new { customerType = "Local", items = new[] { new { item.productVariantId, item.quantity, unitPrice = 1 } } });
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, total.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, price.StatusCode);
    }

    [Theory]
    [InlineData("inactive product")]
    [InlineData("inactive variant")]
    [InlineData("missing")]
    [InlineData("no local")]
    [InlineData("no visitor")]
    [InlineData("local above visitor")]
    public async Task Quote_RejectsUnavailableOrInvalidDiscount(string scenario)
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Cold Brew", "Café");
        var variant = product.AddVariant("Natural", 500);
        if (scenario != "no visitor") variant.AddPrice(CustomerType.Visitor, 60m);
        if (scenario != "no local") variant.AddPrice(CustomerType.Local, scenario == "local above visitor" ? 70m : 50m);
        if (scenario == "inactive product") product.Deactivate();
        if (scenario == "inactive variant") variant.Deactivate();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales/quote", new {
            customerType = "Local", items = new[] { new { productVariantId = scenario == "missing" ? Guid.NewGuid() : variant.Id, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Quote_VisitorNeedsNoLocalPrice_AndReadsUpdatedPrices()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Cold Brew", "Café");
        var variant = product.AddVariant("Natural", 500);
        variant.AddPrice(CustomerType.Visitor, 60m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = new { customerType = "Visitor", items = new[] { new { productVariantId = variant.Id, quantity = 3 } } };
        using var first = await client.PostAsJsonAsync("/api/sales/quote", request);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        // Isolated SQLite only; production PostgreSQL is never touched by this test.
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductPrices SET Amount = 60.25 WHERE ProductVariantId = {variant.Id}");
        using var second = await client.PostAsJsonAsync("/api/sales/quote", request);
        using var json = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal(180.75m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(0m, json.RootElement.GetProperty("discount").GetDecimal());
        Assert.Equal(0, await context.Sales.CountAsync());
    }
}
