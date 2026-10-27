using System.Net;
using System.Net.Http.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Sales.GetDailySalesSummary;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Makari.Api.Tests.Features.Sales.GetDailySalesSummary;

public sealed class PostgreSqlDailySalesSummaryTests
{
    [PostgreSqlFact]
    public async Task SummaryOnMakariTest_UsesAggregatesHistoricalAmountsAndLocalDayBounds()
    {
        var connectionString = Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")!;
        var connection = new NpgsqlConnectionStringBuilder(connectionString);
        Assert.Equal("makari_test", connection.Database);

        using var factory = new MakariTestApiFactory(connectionString);
        using var client = factory.CreateClient();
        var testId = Guid.NewGuid();
        var product = new Product($"M0.8.2-TEST-{testId:N}", "Integration test");
        var lowVariant = product.AddVariant("A-Low", 250);
        lowVariant.AddPrice(CustomerType.Visitor, 1m);
        var highVariant = product.AddVariant("B-High", 500);
        highVariant.AddPrice(CustomerType.Visitor, 1m);
        var idempotencyKeys = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var seeded = false;

        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                Assert.Empty(await db.Database.GetPendingMigrationsAsync());
            }

            var (date, emptySummary) = await FindEmptyDateAsync(client);
            Assert.Equal(HttpStatusCode.OK, emptySummary.StatusCode);
            Assert.Equal(0, emptySummary.Value!.SalesCount);
            Assert.Equal(0m, emptySummary.Value.TotalRevenue);
            Assert.Empty(emptySummary.Value.Variants);

            var startUtc = LocalMidnightUtc(date);
            var endUtc = LocalMidnightUtc(date.AddDays(1));
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                db.Products.Add(product);
                await db.SaveChangesAsync();
                seeded = true;

                AddSale(db, lowVariant.Id, 1, 999m, PaymentMethod.Cash, startUtc.AddSeconds(-1), idempotencyKeys[0]);
                AddSale(db, lowVariant.Id, 1, 10m, PaymentMethod.Cash, startUtc, idempotencyKeys[1]);
                AddSale(db, lowVariant.Id, 1, 20m, PaymentMethod.Cash, startUtc.AddHours(3), idempotencyKeys[2]);
                AddSale(db, highVariant.Id, 2, 25m, PaymentMethod.Transfer, endUtc.AddSeconds(-1), idempotencyKeys[3]);
                AddSale(db, highVariant.Id, 1, 777m, PaymentMethod.Transfer, endUtc, idempotencyKeys[4]);
                await db.SaveChangesAsync();
            }

            var summaryResponse = await client.GetAsync($"/api/sales/summary?date={date:yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);
            var summary = await summaryResponse.Content.ReadFromJsonAsync<GetDailySalesSummaryResponse>();
            Assert.NotNull(summary);
            Assert.Equal(3, summary.SalesCount);
            Assert.Equal(80m, summary.TotalRevenue);
            Assert.Equal(30m, summary.CashRevenue);
            Assert.Equal(50m, summary.TransferRevenue);
            Assert.Equal(4, summary.UnitsSold);
            Assert.Equal(new[] { "B-High", "A-Low" }, summary.Variants.Select(variant => variant.VariantName));
            Assert.Equal(new long[] { 2, 2 }, summary.Variants.Select(variant => variant.Quantity));
            Assert.Equal(new[] { 50m, 30m }, summary.Variants.Select(variant => variant.Revenue));
        }
        finally
        {
            if (seeded)
            {
                using var scope = factory.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                var sales = await db.Sales.Where(sale => idempotencyKeys.Contains(sale.IdempotencyKey)).ToListAsync();
                db.Sales.RemoveRange(sales);
                var prices = await db.ProductPrices.Where(price => price.ProductVariant.ProductId == product.Id).ToListAsync();
                db.ProductPrices.RemoveRange(prices);
                var variants = await db.ProductVariants.Where(variant => variant.ProductId == product.Id).ToListAsync();
                db.ProductVariants.RemoveRange(variants);
                var products = await db.Products.Where(row => row.Id == product.Id).ToListAsync();
                db.Products.RemoveRange(products);
                await db.SaveChangesAsync();
            }
        }
    }

    private static async Task<(DateOnly Date, (HttpStatusCode StatusCode, GetDailySalesSummaryResponse? Value))> FindEmptyDateAsync(HttpClient client)
    {
        for (var day = 1; day <= 28; day++)
        {
            var date = new DateOnly(2091, 1, day);
            using var response = await client.GetAsync($"/api/sales/summary?date={date:yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var summary = await response.Content.ReadFromJsonAsync<GetDailySalesSummaryResponse>();
            Assert.NotNull(summary);
            if (summary.SalesCount == 0) return (date, (response.StatusCode, summary));
        }
        throw new InvalidOperationException("Could not find an empty test day in January 2091 in makari_test.");
    }

    private static DateTimeOffset LocalMidnightUtc(DateOnly date)
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(local, zone);
    }

    private static void AddSale(MakariDbContext db, Guid variantId, int quantity, decimal unitPrice,
        PaymentMethod method, DateTimeOffset createdAt, Guid key)
    {
        var sale = new Sale(CustomerType.Visitor, method, "M0.8.2-TEST", key, Guid.NewGuid().ToString("N"), createdAt);
        sale.AddItem(variantId, quantity, unitPrice);
        db.Sales.Add(sale);
    }
}

internal sealed class PostgreSqlFactAttribute : FactAttribute
{
    public PostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")))
            Skip = "Set MAKARI_TEST_CONNECTION to a connection string whose database is makari_test.";
    }
}

internal sealed class MakariTestApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<MakariDbContext>();
            services.RemoveAll<DbContextOptions<MakariDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MakariDbContext>>();
            services.AddDbContext<MakariDbContext>(options => options.UseNpgsql(connectionString));
        });
    }
}
