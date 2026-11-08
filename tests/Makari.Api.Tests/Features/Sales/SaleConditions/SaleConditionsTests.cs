using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Sales.Persistence;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Sales.SaleConditions;

public sealed class SaleConditionsTests
{
    internal sealed record Item(Guid ProductVariantId, int Quantity);
    internal sealed record Request(Guid IdempotencyKey, string? SaleCondition, string? PaymentMethod, decimal ExpectedTotal, Item[] Items, string? CustomerUsername = null, Guid? CustomerId = null);
    internal static Request NewRequest(Guid id, string? condition = "Normal", decimal total = 120, string? payment = "Cash") => new(Guid.NewGuid(), condition, payment, total, [new(id, 2)]);
    internal static async Task<Guid> Seed(CatalogApiFactory factory)
    {
        var p = new Product("Bebida M14", "Bebidas");
        var v = p.AddVariant("Natural", 350);
        v.AddPrice(CustomerType.Visitor, 60);
        v.AddPrice(CustomerType.Local, 50);
        await factory.InitializeAsync(p);
        return v.Id;
    }
    private static async Task<JsonElement> Body(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        Assert.True(response.StatusCode == expected, $"Expected {expected}, received {response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Theory]
    [InlineData("Local", "Normal", 120)]
    [InlineData("Visitor", "LocalDiscount", 100)]
    public async Task BuyerClassificationDoesNotChoosePrice(string classification, string condition, decimal total)
    {
        using var factory = new CatalogApiFactory();
        var id = await Seed(factory);
        using var client = factory.CreateClient();
        var buyer = new Customer("KnownBuyer");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            db.Customers.Add(buyer); await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await client.PutAsJsonAsync($"/api/customers/{buyer.Id}/classification", new { customerType = classification, expectedCustomerType = "Visitor" })).StatusCode);
        var request = NewRequest(id, condition, total) with { CustomerId = buyer.Id };
        var quote = await Body(await client.PostAsJsonAsync("/api/sales/quote", new { request.SaleCondition, request.Items }));
        Assert.Equal(total, quote.GetProperty("total").GetDecimal());
        var sale = await Body(await client.PostAsJsonAsync("/api/sales", request), HttpStatusCode.Created);
        Assert.Equal(total, sale.GetProperty("total").GetDecimal());
        Assert.Equal(condition, sale.GetProperty("saleCondition").GetString());
        var customer = await Body(await client.GetAsync("/api/customers?username=knownbuyer"));
        Assert.Equal(classification, customer.GetProperty("customerType").GetString());
        if (classification == "Local") Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/customers/{buyer.Id}/classification", new { customerType = "Visitor", expectedCustomerType = "Visitor" })).StatusCode);
    }

    [Theory]
    [InlineData("Courtesy")]
    [InlineData("Staff")]
    [InlineData("Birthday")]
    public async Task FreeConditionsUseReferencePriceConsumeUnitsAndReplayWithoutPayment(string condition)
    {
        using var factory = new CatalogApiFactory();
        var id = await Seed(factory);
        using var client = factory.CreateClient();
        var request = NewRequest(id, condition, 0, null) with { CustomerId = Customer.VisitorId };
        var quote = await Body(await client.PostAsJsonAsync("/api/sales/quote", new { request.SaleCondition, request.Items }));
        Assert.Equal(120, quote.GetProperty("subtotal").GetDecimal());
        Assert.Equal(120, quote.GetProperty("discount").GetDecimal());
        Assert.Equal(0, quote.GetProperty("total").GetDecimal());
        Assert.Equal(2, quote.GetProperty("unitsConsumed").GetInt64());
        var response = await client.PostAsJsonAsync("/api/sales", request);
        var sale = await Body(response, HttpStatusCode.Created);
        Assert.Equal(JsonValueKind.Null, sale.GetProperty("paymentMethod").ValueKind);
        Assert.Equal(60, sale.GetProperty("items")[0].GetProperty("referenceUnitPrice").GetDecimal());
        Assert.Equal(120, sale.GetProperty("items")[0].GetProperty("discount").GetDecimal());
        Assert.Equal(0, sale.GetProperty("total").GetDecimal());
        var replay = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(sale.GetProperty("id").GetGuid(), replayBody.GetProperty("id").GetGuid());
        Assert.Equal(sale.GetProperty("total").GetDecimal(), replayBody.GetProperty("total").GetDecimal());
        Assert.Equal(condition, replayBody.GetProperty("saleCondition").GetString());
        Assert.Equal(JsonValueKind.Null, replayBody.GetProperty("paymentMethod").ValueKind);
        Assert.Equal(sale.GetProperty("items")[0].GetProperty("id").GetGuid(), replayBody.GetProperty("items")[0].GetProperty("id").GetGuid());
        Assert.Equal(120, replayBody.GetProperty("items")[0].GetProperty("discount").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/sales", request with { SaleCondition = condition == "Staff" ? "Birthday" : "Staff" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var stored = await scope.ServiceProvider.GetRequiredService<MakariDbContext>().Sales.Include(s => s.Items).SingleAsync();
        Assert.Null(stored.PaymentMethod);
        Assert.Equal(2, Assert.Single(stored.Items).Quantity);
    }

    [Fact]
    public async Task DefaultNormalSafeClassificationAndInvalidNoChargeRequests()
    {
        using var factory = new CatalogApiFactory();
        var id = await Seed(factory);
        using var client = factory.CreateClient();
        var request = NewRequest(id, null) with { CustomerUsername = "local" };
        var response = await Body(await client.PostAsJsonAsync("/api/sales", request), HttpStatusCode.Created);
        Assert.Equal("Normal", response.GetProperty("saleCondition").GetString());
        Assert.Equal(120, response.GetProperty("total").GetDecimal());
        Assert.Equal("Visitor", (await Body(await client.GetAsync("/api/customers?username=local"))).GetProperty("customerType").GetString());
        foreach (var invalid in new[] { NewRequest(id, "Courtesy", 0, "Cash"), NewRequest(id, "Staff", 10, null), NewRequest(id, "Normal", 120, null), NewRequest(id, "Unknown") })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/sales", invalid)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/customers/{Customer.LocalId}/classification", new { customerType = "Other", expectedCustomerType = "Visitor" })).StatusCode);
    }

    [Fact]
    public async Task DailySummaryAndCloseSeparateUnitsFromMoneyAndBlockFreeSalesAfterClose()
    {
        using var factory = new CatalogApiFactory();
        var id = await Seed(factory);
        using var client = factory.CreateClient();
        var requests = new[] { NewRequest(id), NewRequest(id, "LocalDiscount", 100, "Card"), NewRequest(id, "Courtesy", 0, null), NewRequest(id, "Staff", 0, null), NewRequest(id, "Birthday", 0, null) };
        foreach (var request in requests) Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/sales", request)).StatusCode);
        var summary = await Body(await client.GetAsync("/api/sales/summary"));
        Assert.Equal(10, summary.GetProperty("unitsRegistered").GetInt64());
        Assert.Equal(4, summary.GetProperty("paidUnits").GetInt64());
        Assert.Equal(6, summary.GetProperty("freeUnits").GetInt64());
        Assert.Equal(220, summary.GetProperty("totalRevenue").GetDecimal());
        Assert.All(summary.GetProperty("conditions").EnumerateArray(), c => { Assert.Equal(1, c.GetProperty("operations").GetInt32()); Assert.Equal(2, c.GetProperty("units").GetInt64()); });
        var close = await Body(await client.PostAsJsonAsync("/api/cash-closes", new { idempotencyKey = Guid.NewGuid(), businessDate = summary.GetProperty("date").GetString(), openingCash = 10, countedCash = 130, responsible = "M14" }), HttpStatusCode.Created);
        Assert.Equal(120, close.GetProperty("cashSales").GetDecimal());
        Assert.Equal(100, close.GetProperty("cardSales").GetDecimal());
        Assert.Equal(0, close.GetProperty("transferSales").GetDecimal());
        Assert.Equal(130, close.GetProperty("expectedCash").GetDecimal());
        Assert.Equal(0, close.GetProperty("difference").GetDecimal());
        var rejected = await client.PostAsJsonAsync("/api/sales", NewRequest(id, "Staff", 0, null));
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("daily_cash_closed", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sales", requests[2])).StatusCode);
        var storedClose = await Body(await client.GetAsync($"/api/cash-closes?date={summary.GetProperty("date").GetString()}"));
        Assert.Equal(close.GetProperty("expectedCash").GetDecimal(), storedClose.GetProperty("expectedCash").GetDecimal());
    }

    [Fact]
    public async Task ExistingFingerprintsAndRejectedKeysKeepLegacySemantics()
    {
        using var factory = new CatalogApiFactory();
        var id = await Seed(factory);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid(); var rejectedKey = Guid.NewGuid();
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"0|2|{id:N}:2")));
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            var sale = new Sale(CustomerType.Local, PaymentMethod.Card, "Before migration", key, fingerprint, DateTimeOffset.UtcNow);
            sale.AddItem(id, 2, 45); // Historical amount no longer matches the catalog.
            db.Sales.Add(sale);
            db.Set<RejectedSaleAttempt>().Add(new() { IdempotencyKey = rejectedKey, RequestFingerprint = fingerprint, ExpectedTotal = 80, CurrentTotal = 90 });
            await db.SaveChangesAsync();
        }
        var request = new { idempotencyKey = key, customerType = "Local", paymentMethod = "Card", expectedTotal = 90, items = new[] { new { productVariantId = id, quantity = 2 } } };
        var replay = await Body(await client.PostAsJsonAsync("/api/sales", request));
        Assert.Equal(90, replay.GetProperty("total").GetDecimal());
        Assert.Equal("LocalDiscount", replay.GetProperty("saleCondition").GetString());
        var rejected = await client.PostAsJsonAsync("/api/sales", request with { idempotencyKey = rejectedKey, expectedTotal = 80 });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        Assert.Contains("\"currentTotal\":90", await rejected.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/sales", NewRequest(id, "LocalDiscount", 90, "Card") with { IdempotencyKey = key })).StatusCode);
        var pending = NewRequest(id, "Normal", 80);
        var failed = await client.PostAsJsonAsync("/api/sales", pending);
        Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);
        Assert.Equal(await failed.Content.ReadAsStringAsync(), await (await client.PostAsJsonAsync("/api/sales", pending)).Content.ReadAsStringAsync());
        var changed = await client.PostAsJsonAsync("/api/sales", pending with { SaleCondition = "Courtesy", ExpectedTotal = 0, PaymentMethod = null });
        Assert.Contains("idempotency_conflict", await changed.Content.ReadAsStringAsync());
    }
}
