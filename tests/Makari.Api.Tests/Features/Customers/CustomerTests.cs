using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makari.Api.Features.Customers;
using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Customers;

public sealed class CustomerTests
{
    [Theory]
    [InlineData(null, "visitante", "Visitor", 60)]
    [InlineData("", "visitante", "Visitor", 60)]
    [InlineData("   ", "visitante", "Visitor", 60)]
    [InlineData(" LOCAL ", "local", "Local", 50)]
    [InlineData("local", "local", "Visitor", 60)]
    [InlineData(" Ana   Luna ", "Ana Luna", "Local", 50)]
    public async Task Sale_ResolvesBuyerWithoutRegistration(string? username, string expected, string priceType, decimal total)
    {
        using var factory = new CatalogApiFactory();
        var (product, variantId) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales", Request(variantId, username, total) with { customerType = priceType });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expected, json.RootElement.GetProperty("customerUsernameSnapshot").GetString());
        Assert.NotEqual(Guid.Empty, json.RootElement.GetProperty("customerId").GetGuid());
        Assert.Equal(priceType, json.RootElement.GetProperty("customerType").GetString());
    }

    [Fact]
    public async Task NamedBuyer_NormalizesReusesAndReplays_ConflictsForDifferentUsername()
    {
        using var factory = new CatalogApiFactory();
        var (product, variantId) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = Request(variantId, "  Ana   Luna ");
        using var first = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var retry = await client.PostAsJsonAsync("/api/sales", request with { customerUsername = "ana luna" });
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        using var second = await client.PostAsJsonAsync("/api/sales", request with { idempotencyKey = Guid.NewGuid(), customerUsername = "ANA LUNA" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        using var conflict = await client.PostAsJsonAsync("/api/sales", request with { customerUsername = "Otro" });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(2, await db.Sales.CountAsync());
        var customer = await db.Customers.SingleAsync(row => row.NormalizedUsername == "ana luna");
        Assert.Equal("Ana Luna", customer.Username);
        Assert.All(await db.Sales.ToListAsync(), sale => Assert.Equal(customer.Id, sale.CustomerId));
        db.Customers.Add(new Customer("ANA LUNA"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("@invalid")]
    [InlineData("a/other")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task InvalidUsername_DoesNotCreateBuyer(string username)
    {
        using var factory = new CatalogApiFactory();
        var (product, variantId) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/sales", Request(variantId, username));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<MakariDbContext>().Customers.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RejectedSale_DoesNotCreateBuyer_AndReplaysRejection(bool missingVariant)
    {
        using var factory = new CatalogApiFactory();
        var (product, variantId) = Product();
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = Request(missingVariant ? Guid.NewGuid() : variantId, "No orphan", 1m);
        using var first = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(missingVariant ? HttpStatusCode.BadRequest : HttpStatusCode.Conflict, first.StatusCode);
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(2, await db.Customers.CountAsync());
        Assert.Empty(await db.Sales.ToListAsync());
    }

    [Fact]
    public async Task FailedSaleInsert_RollsBackNewBuyer()
    {
        using var factory = new CatalogApiFactory();
        var (product, variantId) = Product();
        await factory.InitializeAsync(product);
        await factory.InstallSaleItemFailureAsync();
        using var client = factory.CreateClient();
        try { using var response = await client.PostAsJsonAsync("/api/sales", Request(variantId, "Rolled back")); Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode); }
        catch (DbUpdateException) { }
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(2, await db.Customers.CountAsync());
        Assert.Empty(await db.Sales.ToListAsync());
    }

    private static (Product Product, Guid VariantId) Product()
    {
        var product = new Product("Customer test", "Test");
        var variant = product.AddVariant("Bottle", 300);
        variant.AddPrice(CustomerType.Visitor, 60m);
        variant.AddPrice(CustomerType.Local, 50m);
        return (product, variant.Id);
    }

    private sealed record SaleRequest(Guid idempotencyKey, string customerType, string paymentMethod,
        decimal expectedTotal, string? customerUsername, object[] items);
    private static SaleRequest Request(Guid variantId, string? username, decimal total = 60m) =>
        new(Guid.NewGuid(), "Visitor", "Cash", total, username, [new { productVariantId = variantId, quantity = 1 }]);

    [Fact]
    public async Task Sale_BuyerSnapshotAndPriceTypeAreIndependent_IdentityParticipatesInIdempotency()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Buyer test", "Test");
        var variant = product.AddVariant("Bottle", 300);
        variant.AddPrice(CustomerType.Visitor, 60m);
        variant.AddPrice(CustomerType.Local, 50m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = new { idempotencyKey = Guid.NewGuid(), customerType = "Visitor", paymentMethod = "Card", expectedTotal = 60m,
            customerId = Customer.LocalId, items = new[] { new { productVariantId = variant.Id, quantity = 1 } } };
        using var response = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("local", json.RootElement.GetProperty("customerUsernameSnapshot").GetString());
        Assert.Equal(Customer.LocalId, json.RootElement.GetProperty("customerId").GetGuid());
        Assert.Equal("Visitor", json.RootElement.GetProperty("customerType").GetString());
        Assert.Equal(60m, json.RootElement.GetProperty("total").GetDecimal());
        Assert.Equal("Andi", json.RootElement.GetProperty("registeredBy").GetString());
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        using var conflict = await client.PostAsJsonAsync("/api/sales", request with { customerId = Customer.VisitorId });
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        using var conflictJson = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync());
        Assert.Equal("idempotency_conflict", conflictJson.RootElement.GetProperty("code").GetString());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var saved = await db.Sales.AsNoTracking().SingleAsync();
        Assert.Equal(Customer.LocalId, saved.CustomerId);
        Assert.Equal("local", saved.CustomerUsernameSnapshot);
        // Isolated test-only rename verifies the response is a snapshot, not a live join.
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE Customers SET Username = {"Renamed"} WHERE Id = {Customer.LocalId}");
        using var summaryResponse = await client.GetAsync("/api/sales/summary");
        using var summary = JsonDocument.Parse(await summaryResponse.Content.ReadAsStringAsync());
        using var close = await client.PostAsJsonAsync("/api/cash-closes", new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = summary.RootElement.GetProperty("date").GetString(),
            openingCash = 0m, countedCash = 0m, responsible = "Andi", observations = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, close.StatusCode);
        using var afterClose = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.OK, afterClose.StatusCode);
        Assert.Equal(await response.Content.ReadAsStringAsync(), await afterClose.Content.ReadAsStringAsync());
        using var blocked = await client.PostAsJsonAsync("/api/sales", request with { idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var blockedJson = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("daily_cash_closed", blockedJson.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MissingBuyer_RejectionIsPersistedAndCannotChangeIdentityOnRetry()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var request = new { idempotencyKey = Guid.NewGuid(), customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 60m,
            customerId = Guid.NewGuid(), items = new[] { new { productVariantId = Guid.NewGuid(), quantity = 1 } } };
        using var first = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.BadRequest, first.StatusCode);
        using var json = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("errors").TryGetProperty("customerId", out _));
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(await first.Content.ReadAsStringAsync(), await retry.Content.ReadAsStringAsync());
        using var changed = await client.PostAsJsonAsync("/api/sales", request with { customerId = Customer.VisitorId });
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task HistoricalSaleWithoutBuyer_ReplaysOriginalFingerprintAndNullIdentity()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Historical buyer test", "Test");
        var variant = product.AddVariant("Bottle", 300);
        variant.AddPrice(CustomerType.Visitor, 60m);
        await factory.InitializeAsync(product);
        var key = Guid.NewGuid();
        // Exact pre-M1.2 canonical form, independent of the current handler.
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"1|0|{variant.Id:N}:1")));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            var historical = new Sale(CustomerType.Visitor, PaymentMethod.Cash, "Historical operator", key, fingerprint, DateTimeOffset.UtcNow);
            historical.AddItem(variant.Id, 1, 60m);
            db.Sales.Add(historical);
            await db.SaveChangesAsync();
        }
        using var client = factory.CreateClient();
        var request = new { idempotencyKey = key, customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 60m,
            items = new[] { new { productVariantId = variant.Id, quantity = 1 } } };
        using var retry = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        using var json = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("customerId").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("customerUsernameSnapshot").ValueKind);
        Assert.Equal("Historical operator", json.RootElement.GetProperty("registeredBy").GetString());
    }
}
