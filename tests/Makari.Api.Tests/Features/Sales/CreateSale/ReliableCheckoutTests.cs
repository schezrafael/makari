using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Sales.CreateSale;

public sealed class ReliableCheckoutTests
{
    [Fact]
    public async Task ExpectedTotal_IsRequiredInTheHttpContract()
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid(), customerType = "Visitor", paymentMethod = "Cash",
            items = new[] { new { productVariantId = Guid.NewGuid(), quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(60, HttpStatusCode.Created)]
    [InlineData(59, HttpStatusCode.Conflict)]
    [InlineData(61, HttpStatusCode.Conflict)]
    public async Task ExpectedTotal_MustMatchCurrentPrice(decimal expectedTotal, HttpStatusCode expectedStatus)
    {
        using var factory = new CatalogApiFactory();
        var (product, id) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        using var response = await client.PostAsJsonAsync("/api/sales", Request(id, key, expectedTotal));
        Assert.Equal(expectedStatus, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(expectedStatus == HttpStatusCode.Created ? 1 : 0, await db.Sales.CountAsync());
        if (expectedStatus == HttpStatusCode.Conflict)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("price_changed", json.RootElement.GetProperty("code").GetString());
            Assert.Equal(60m, json.RootElement.GetProperty("currentTotal").GetDecimal());
            Assert.Equal(key, json.RootElement.GetProperty("idempotencyKey").GetGuid());
            Assert.Equal(0, await db.SaleItems.CountAsync());
        }
    }

    [Fact]
    public async Task PriceChange_AfterQuoteRequiresNewConfirmation_ExistingReplayIgnoresCurrentPrices()
    {
        using var factory = new CatalogApiFactory();
        var (product, id) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        using var quote = await client.PostAsJsonAsync("/api/sales/quote", new { customerType = "Visitor", items = new[] { new { productVariantId = id, quantity = 1 } } });
        using var quoted = JsonDocument.Parse(await quote.Content.ReadAsStringAsync());
        var expected = quoted.RootElement.GetProperty("total").GetDecimal();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        // Only the isolated in-memory test database is changed.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductPrices SET Amount = 65 WHERE ProductVariantId = {id}");
        using var rejected = await client.PostAsJsonAsync("/api/sales", Request(id, Guid.NewGuid(), expected));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Equal(0, await db.Sales.CountAsync());
        var request = Request(id, Guid.NewGuid(), 65);
        using var created = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductPrices SET Amount = 70 WHERE ProductVariantId = {id}");
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductVariants SET IsActive = 0 WHERE Id = {id}");
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var original = JsonDocument.Parse(await created.Content.ReadAsStringAsync());
        using var replay = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(original.RootElement.GetProperty("id").GetGuid(), replay.RootElement.GetProperty("id").GetGuid());
        Assert.Equal(65m, replay.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal(1, await db.Sales.CountAsync());
    }

    [Fact]
    public async Task ExistingKey_WithDifferentExpectedTotalIsIdempotencyConflict_NotPriceChange()
    {
        using var factory = new CatalogApiFactory();
        var (product, id) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        using var created = await client.PostAsJsonAsync("/api/sales", Request(id, key, 60));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var changed = await client.PostAsJsonAsync("/api/sales", Request(id, key, 61));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var json = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal("idempotency_conflict", json.RootElement.GetProperty("code").GetString());
        Assert.False(json.RootElement.TryGetProperty("currentTotal", out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(60.001)]
    public async Task ExpectedTotal_RequiredPositiveAndTwoDecimals(double? expected)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales", Request(Guid.NewGuid(), Guid.NewGuid(), (decimal?)expected));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedKey_IsTerminalEvenWhenPricesOrAvailabilityLaterAllowSale(bool unavailable)
    {
        using var factory = new CatalogApiFactory();
        var (product, id) = Product();
        if (unavailable) product.Deactivate();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        var request = Request(id, key, 50m);
        using var rejected = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(unavailable ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, rejected.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductPrices SET Amount = 50 WHERE ProductVariantId = {id}");
        await db.Database.ExecuteSqlRawAsync("UPDATE Products SET IsActive = 1");
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(rejected.StatusCode, retry.StatusCode);
        Assert.Equal(await rejected.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        Assert.Equal(0, await db.Sales.CountAsync());
        using var changed = await client.PostAsJsonAsync("/api/sales", Request(id, key, 60m));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        using var body = JsonDocument.Parse(await changed.Content.ReadAsStringAsync());
        Assert.Equal("idempotency_conflict", body.RootElement.GetProperty("code").GetString());
        using var confirmed = await client.PostAsJsonAsync("/api/sales", Request(id, Guid.NewGuid(), 50m));
        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
    }

    private static object Request(Guid id, Guid key, decimal? expectedTotal) => new {
        idempotencyKey = key, customerType = "Visitor", paymentMethod = "Cash", expectedTotal,
        items = new[] { new { productVariantId = id, quantity = 1 } }
    };

    private static (Product Product, Guid Id) Product()
    {
        var product = new Product("Test", "Coffee");
        var variant = product.AddVariant("Natural", 500);
        variant.AddPrice(CustomerType.Visitor, 60m);
        return (product, variant.Id);
    }
}
