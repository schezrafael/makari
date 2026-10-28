using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.CreateSale;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Sales.GetDailySalesSummary;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.CashCloses;

public sealed class CreateDailyCashCloseTests
{
    [Fact]
    public async Task EmptyDay_AllowsZeroOpeningCash_AndCloseIsIdempotentAndReadable()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var date = await BusinessTodayAsync(client);
        using (var missing = await client.GetAsync($"/api/cash-closes?date={date:yyyy-MM-dd}"))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var request = new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = date, openingCash = 0m, countedCash = 0m,
            responsible = "Andi", observations = (string?)null
        };

        using var created = await client.PostAsJsonAsync("/api/cash-closes", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var close = (await created.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
        Assert.Equal(date, close.BusinessDate);
        Assert.Equal(0, close.SalesCount);
        Assert.Equal(0m, close.OpeningCash);
        Assert.Equal(0m, close.ExpectedCash);
        Assert.Equal(0m, close.Difference);

        using var retry = await client.PostAsJsonAsync("/api/cash-closes", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(close.Id, (await retry.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!.Id);
        using var changedRetry = await client.PostAsJsonAsync("/api/cash-closes", request with { countedCash = 10m });
        Assert.Equal(HttpStatusCode.Conflict, changedRetry.StatusCode);
        using (var changedJson = JsonDocument.Parse(await changedRetry.Content.ReadAsStringAsync()))
            Assert.Equal("idempotency_conflict", changedJson.RootElement.GetProperty("code").GetString());

        using var byDate = await client.GetAsync($"/api/cash-closes?date={date:yyyy-MM-dd}");
        using var byId = await client.GetAsync($"/api/cash-closes/{close.Id}");
        Assert.Equal(HttpStatusCode.OK, byDate.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byId.StatusCode);
        Assert.Equal(close.Id, (await byId.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!.Id);

        using var duplicate = await client.PostAsJsonAsync("/api/cash-closes", request with { idempotencyKey = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var conflict = JsonDocument.Parse(await duplicate.Content.ReadAsStringAsync());
        Assert.Equal("cash_close_exists", conflict.RootElement.GetProperty("code").GetString());
        Assert.Equal(close.Id, conflict.RootElement.GetProperty("existingCloseId").GetGuid());
    }

    [Fact]
    public async Task CloseTotals_UseTheBusinessLocalSemiOpenDayInterval()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Close boundary product", "Test");
        var variant = product.AddVariant("Bottle", 500);
        variant.AddPrice(CustomerType.Visitor, 1m);
        await factory.InitializeAsync(product);
        var date = new DateOnly(2026, 6, 10);
        var start = LocalMidnightUtc(date);
        var end = LocalMidnightUtc(date.AddDays(1));
        await InsertSaleAsync(factory, variant.Id, 900m, PaymentMethod.Cash, start.AddSeconds(-1));
        await InsertSaleAsync(factory, variant.Id, 10m, PaymentMethod.Cash, start);
        await InsertSaleAsync(factory, variant.Id, 20m, PaymentMethod.Transfer, end.AddSeconds(-1));
        await InsertSaleAsync(factory, variant.Id, 700m, PaymentMethod.Transfer, end);

        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/cash-closes", new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = date, openingCash = 5m, countedCash = 15m,
            responsible = "Andi", observations = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var close = (await response.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
        Assert.Equal(2, close.SalesCount);
        Assert.Equal(10m, close.CashSales);
        Assert.Equal(20m, close.TransferSales);
        Assert.Equal(30m, close.TotalRevenue);
        Assert.Equal(15m, close.ExpectedCash);
        Assert.Equal(0m, close.Difference);
    }

    [Theory]
    [InlineData(15, 90, 15)]
    [InlineData(15, 55, -20)]
    public async Task Close_SnapshotsCashAndTransferSalesAndCalculatesDifference(
        decimal openingCash, decimal countedCash, decimal expectedDifference)
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Cash close test drink", "Test");
        var variant = product.AddVariant("Bottle", 500);
        variant.AddPrice(CustomerType.Visitor, 60m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        await CreateSaleAsync(client, variant.Id, PaymentMethod.Cash, 60m);
        await CreateSaleAsync(client, variant.Id, PaymentMethod.Transfer, 60m);
        var date = await BusinessTodayAsync(client);
        var request = new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = date, openingCash, countedCash,
            responsible = "Rafael", observations = "Revisión diaria"
        };
        using var created = await client.PostAsJsonAsync("/api/cash-closes", request);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var close = (await created.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
        Assert.Equal(2, close.SalesCount);
        Assert.Equal(60m, close.CashSales);
        Assert.Equal(60m, close.TransferSales);
        Assert.Equal(120m, close.TotalRevenue);
        Assert.Equal(openingCash + 60m, close.ExpectedCash);
        Assert.Equal(countedCash, close.CountedCash);
        Assert.Equal(expectedDifference, close.Difference);
        Assert.Equal("Rafael", close.Responsible);
        Assert.Equal("Revisión diaria", close.Observations);
    }

    [Fact]
    public async Task ClosedBusinessDate_BlocksNewSalesButReplaysPreviouslyConfirmedSale()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Close rule product", "Test");
        var variant = product.AddVariant("Bottle", 500);
        variant.AddPrice(CustomerType.Visitor, 60m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var saleKey = Guid.NewGuid();
        var saleRequest = new
        {
            idempotencyKey = saleKey, customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 60m,
            items = new[] { new { productVariantId = variant.Id, quantity = 1 } }
        };
        using var initialSale = await client.PostAsJsonAsync("/api/sales", saleRequest);
        Assert.Equal(HttpStatusCode.Created, initialSale.StatusCode);
        var date = await BusinessTodayAsync(client);
        using var closeResponse = await client.PostAsJsonAsync("/api/cash-closes", new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = date, openingCash = 0m, countedCash = 60m,
            responsible = "Andi", observations = (string?)null
        });
        Assert.Equal(HttpStatusCode.Created, closeResponse.StatusCode);

        using var replay = await client.PostAsJsonAsync("/api/sales", saleRequest);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        using var replayJson = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
        using var originalJson = JsonDocument.Parse(await initialSale.Content.ReadAsStringAsync());
        Assert.Equal(originalJson.RootElement.GetProperty("id").GetGuid(), replayJson.RootElement.GetProperty("id").GetGuid());

        var blockedKey = Guid.NewGuid();
        using var blocked = await client.PostAsJsonAsync("/api/sales", saleRequest with { idempotencyKey = blockedKey });
        Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        using var blockedJson = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
        Assert.Equal("daily_cash_closed", blockedJson.RootElement.GetProperty("code").GetString());
        Assert.Equal(date, DateOnly.Parse(blockedJson.RootElement.GetProperty("businessDate").GetString()!));

        using var blockedRetry = await client.PostAsJsonAsync("/api/sales", saleRequest with { idempotencyKey = blockedKey });
        Assert.Equal(HttpStatusCode.Conflict, blockedRetry.StatusCode);
        Assert.Equal(await blocked.Content.ReadAsStringAsync(), await blockedRetry.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(1, await db.Sales.CountAsync());
    }

    [Fact]
    public async Task RejectsFutureDatesNegativeAmountsAndInvalidPrecision()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var date = await BusinessTodayAsync(client);
        var valid = new
        {
            idempotencyKey = Guid.NewGuid(), businessDate = date.AddDays(1), openingCash = 0m,
            countedCash = 0m, responsible = "Andi", observations = (string?)null
        };
        using var future = await client.PostAsJsonAsync("/api/cash-closes", valid);
        Assert.Equal(HttpStatusCode.BadRequest, future.StatusCode);
        using var negative = await client.PostAsJsonAsync("/api/cash-closes", valid with { businessDate = date, openingCash = -1m });
        Assert.Equal(HttpStatusCode.BadRequest, negative.StatusCode);
        using var precision = await client.PostAsJsonAsync("/api/cash-closes", valid with { businessDate = date, countedCash = 1.001m });
        Assert.Equal(HttpStatusCode.BadRequest, precision.StatusCode);
        using var noResponsible = await client.PostAsJsonAsync("/api/cash-closes", valid with { businessDate = date, responsible = " " });
        Assert.Equal(HttpStatusCode.BadRequest, noResponsible.StatusCode);
        using var missingDate = await client.PostAsJsonAsync("/api/cash-closes", new
        {
            idempotencyKey = Guid.NewGuid(), openingCash = 0m, countedCash = 0m,
            responsible = "Andi", observations = (string?)null
        });
        Assert.Equal(HttpStatusCode.BadRequest, missingDate.StatusCode);
    }

    private static async Task<DateOnly> BusinessTodayAsync(HttpClient client)
    {
        var summary = (await client.GetFromJsonAsync<GetDailySalesSummaryResponse>("/api/sales/summary"))!;
        return summary.Date;
    }

    private static async Task CreateSaleAsync(HttpClient client, Guid variantId, PaymentMethod method, decimal total)
    {
        using var response = await client.PostAsJsonAsync("/api/sales", new
        {
            idempotencyKey = Guid.NewGuid(), customerType = "Visitor", paymentMethod = method.ToString(),
            expectedTotal = total, items = new[] { new { productVariantId = variantId, quantity = 1 } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static async Task InsertSaleAsync(CatalogApiFactory factory, Guid variantId, decimal unitPrice,
        PaymentMethod method, DateTimeOffset createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var sale = new Sale(CustomerType.Visitor, method, "cash-close-test", Guid.NewGuid(), Guid.NewGuid().ToString("N"), createdAt);
        sale.AddItem(variantId, 1, unitPrice);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
    }
}
