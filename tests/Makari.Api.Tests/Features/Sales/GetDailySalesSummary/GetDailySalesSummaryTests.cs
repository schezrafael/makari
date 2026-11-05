using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Sales.GetDailySalesSummary;

public sealed class GetDailySalesSummaryTests
{
    private static readonly TimeZoneInfo BusinessZone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");

    [Fact]
    public async Task EmptyDay_ReturnsZeroesAndEmptyVariants()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/sales/summary?date=2026-06-10");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(0, body.GetProperty("salesCount").GetInt32());
        Assert.Equal(0m, body.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(0m, body.GetProperty("cashRevenue").GetDecimal());
        Assert.Equal(0m, body.GetProperty("transferRevenue").GetDecimal());
        Assert.Equal(0m, body.GetProperty("cardRevenue").GetDecimal());
        Assert.Equal(0, body.GetProperty("unitsSold").GetInt64());
        Assert.Empty(body.GetProperty("variants").EnumerateArray());
    }

    [Fact]
    public async Task Summary_UsesLocalDayHistoricalSubtotalsAndGroupsByVariant()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Café", "Bebidas");
        var small = product.AddVariant("Chico", 250);
        small.AddPrice(CustomerType.Visitor, 99m); // current catalog price must not define historical revenue
        var large = product.AddVariant("Grande", 500);
        large.AddPrice(CustomerType.Visitor, 120m);
        await factory.InitializeAsync(product);

        var start = LocalMidnight(new DateOnly(2026, 6, 10));
        await AddSale(factory, small.Id, 1, 40m, PaymentMethod.Cash, start.AddTicks(-1));
        await AddSale(factory, small.Id, 2, 35m, PaymentMethod.Cash, start);
        await AddSale(factory, large.Id, 1, 75m, PaymentMethod.Transfer, start.AddHours(12));
        await AddSale(factory, large.Id, 3, 80m, PaymentMethod.Transfer, LocalMidnight(new DateOnly(2026, 6, 11)).AddTicks(-1));
        await AddSale(factory, large.Id, 1, 90m, PaymentMethod.Cash, LocalMidnight(new DateOnly(2026, 6, 11)));

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/sales/summary?date=2026-06-10");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var body = json.RootElement;
        Assert.Equal(3, body.GetProperty("salesCount").GetInt32());
        Assert.Equal(385m, body.GetProperty("totalRevenue").GetDecimal());
        Assert.Equal(70m, body.GetProperty("cashRevenue").GetDecimal());
        Assert.Equal(315m, body.GetProperty("transferRevenue").GetDecimal());
        Assert.Equal(6, body.GetProperty("unitsSold").GetInt64());
        var variants = body.GetProperty("variants").EnumerateArray().ToArray();
        Assert.Equal(2, variants.Length);
        Assert.Contains(variants, item => item.GetProperty("variantName").GetString() == "Chico"
            && item.GetProperty("quantity").GetInt64() == 2 && item.GetProperty("revenue").GetDecimal() == 70m);
        Assert.Contains(variants, item => item.GetProperty("variantName").GetString() == "Grande"
            && item.GetProperty("quantity").GetInt64() == 4 && item.GetProperty("revenue").GetDecimal() == 315m);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("10-06-2026")]
    [InlineData("")]
    public async Task InvalidDate_ReturnsBadRequest(string date)
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/sales/summary?date={Uri.EscapeDataString(date)}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OmittedDate_UsesBusinessTimezoneToday()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/sales/summary");
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, BusinessZone).DateTime);
        Assert.Equal(localToday, DateOnly.Parse(json.RootElement.GetProperty("date").GetString()!));
        Assert.Equal("America/Mexico_City", json.RootElement.GetProperty("timeZoneId").GetString());
    }

    private static DateTimeOffset LocalMidnight(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, BusinessZone);
    }

    private static async Task AddSale(CatalogApiFactory factory, Guid variantId, int quantity, decimal unitPrice,
        PaymentMethod payment, DateTimeOffset createdAt)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var sale = new Sale(CustomerType.Visitor, payment, "summary-test", Guid.NewGuid(), Guid.NewGuid().ToString("N"), createdAt);
        sale.AddItem(variantId, quantity, unitPrice);
        db.Sales.Add(sale);
        await db.SaveChangesAsync();
    }
}
