using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.CreateSale;
using Makari.Api.Features.Sales.GetDailySalesSummary;
using Makari.Api.Features.Sales.Persistence;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;

namespace Makari.Api.Tests.Features.CashCloses;

public sealed class PostgreSqlCashCloseTests
{
    [PostgreSqlCashCloseFact]
    public async Task TwoConcurrentClosesForSameDate_ConfirmAtMostOne()
    {
        var connectionString = TestConnection();
        using var factory = new PostgreSqlCashCloseApiFactory(connectionString);
        using var client = factory.CreateClient();
        await AssertReadyAsync(factory);
        var date = await FindUnclosedDateAsync(client, 2001);
        var firstKey = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        try
        {
            var firstTask = client.PostAsJsonAsync("/api/cash-closes", Request(firstKey, date, "Andi"));
            var secondTask = client.PostAsJsonAsync("/api/cash-closes", Request(secondKey, date, "Rafael"));
            using var first = await firstTask;
            using var second = await secondTask;
            Assert.Contains(new[] { first.StatusCode, second.StatusCode }, status => status == HttpStatusCode.Created);
            Assert.Contains(new[] { first.StatusCode, second.StatusCode }, status => status == HttpStatusCode.Conflict);
            Assert.Single(new[] { first.StatusCode, second.StatusCode }, status => status == HttpStatusCode.Created);

            using var byDate = await client.GetAsync($"/api/cash-closes?date={date:yyyy-MM-dd}");
            Assert.Equal(HttpStatusCode.OK, byDate.StatusCode);
            var saved = (await byDate.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
            Assert.Contains(saved.IdempotencyKey, new[] { firstKey, secondKey });
        }
        finally
        {
            await DeleteClosesAsync(factory, firstKey, secondKey);
        }
    }

    [PostgreSqlCashCloseFact]
    public async Task SaleRacingClose_IsIncludedOrRejectedButNeverOmitted()
    {
        var connectionString = TestConnection();
        using var factory = new PostgreSqlCashCloseApiFactory(connectionString);
        using var client = factory.CreateClient();
        await AssertReadyAsync(factory);
        var before = (await client.GetFromJsonAsync<GetDailySalesSummaryResponse>("/api/sales/summary"))!;
        using (var existing = await client.GetAsync($"/api/cash-closes?date={before.Date:yyyy-MM-dd}"))
            Assert.Equal(HttpStatusCode.NotFound, existing.StatusCode);

        var testId = Guid.NewGuid();
        var product = new Product($"M0.9-TEST-{testId:N}", "Integration test");
        var variant = product.AddVariant("Race variant", 300);
        variant.AddPrice(CustomerType.Visitor, 61m);
        var saleKey = Guid.NewGuid();
        var closeKey = Guid.NewGuid();
        var seeded = false;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                db.Products.Add(product);
                await db.SaveChangesAsync();
                seeded = true;
            }

            var saleRequest = new
            {
                idempotencyKey = saleKey, customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 61m,
                items = new[] { new { productVariantId = variant.Id, quantity = 1 } }
            };
            var closeRequest = Request(closeKey, before.Date, "M0.9 race test");
            var saleTask = client.PostAsJsonAsync("/api/sales", saleRequest);
            var closeTask = client.PostAsJsonAsync("/api/cash-closes", closeRequest);
            using var saleResponse = await saleTask;
            using var closeResponse = await closeTask;

            Assert.Equal(HttpStatusCode.Created, closeResponse.StatusCode);
            var close = (await closeResponse.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
            if (saleResponse.StatusCode == HttpStatusCode.Created)
            {
                Assert.Equal(before.SalesCount + 1, close.SalesCount);
                Assert.Equal(before.TotalRevenue + 61m, close.TotalRevenue);
                Assert.Equal(before.CashRevenue + 61m, close.CashSales);
                using var replay = await client.PostAsJsonAsync("/api/sales", saleRequest);
                Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            }
            else
            {
                Assert.Equal(HttpStatusCode.Conflict, saleResponse.StatusCode);
                using var rejected = JsonDocument.Parse(await saleResponse.Content.ReadAsStringAsync());
                Assert.Equal("daily_cash_closed", rejected.RootElement.GetProperty("code").GetString());
                Assert.Equal(before.SalesCount, close.SalesCount);
                Assert.Equal(before.TotalRevenue, close.TotalRevenue);
                Assert.Equal(before.CashRevenue, close.CashSales);
            }
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            if (seeded)
            {
                var sales = await db.Sales.Where(sale => sale.IdempotencyKey == saleKey).ToListAsync();
                db.Sales.RemoveRange(sales);
                var rejected = await db.Set<RejectedSaleAttempt>().Where(attempt => attempt.IdempotencyKey == saleKey).ToListAsync();
                db.Set<RejectedSaleAttempt>().RemoveRange(rejected);
                var closes = await db.DailyCashCloses.Where(close => close.IdempotencyKey == closeKey).ToListAsync();
                db.DailyCashCloses.RemoveRange(closes);
                var prices = await db.ProductPrices.Where(price => price.ProductVariant.ProductId == product.Id).ToListAsync();
                db.ProductPrices.RemoveRange(prices);
                var variants = await db.ProductVariants.Where(row => row.ProductId == product.Id).ToListAsync();
                db.ProductVariants.RemoveRange(variants);
                var products = await db.Products.Where(row => row.Id == product.Id).ToListAsync();
                db.Products.RemoveRange(products);
                await db.SaveChangesAsync();
            }
        }
    }

    [PostgreSqlCashCloseFact]
    public async Task CardSale_IsPersistedReplayedAndIncludedInSummaryAndClose()
    {
        var connectionString = TestConnection();
        using var factory = new PostgreSqlCashCloseApiFactory(connectionString);
        using var client = factory.CreateClient();
        await AssertReadyAsync(factory);
        var before = (await client.GetFromJsonAsync<GetDailySalesSummaryResponse>("/api/sales/summary"))!;
        using (var existing = await client.GetAsync($"/api/cash-closes?date={before.Date:yyyy-MM-dd}"))
            Assert.Equal(HttpStatusCode.NotFound, existing.StatusCode);

        var testId = Guid.NewGuid();
        var product = new Product($"M1.1-CARD-TEST-{testId:N}", "Integration test");
        var variant = product.AddVariant("Race variant", 300);
        variant.AddPrice(CustomerType.Visitor, 61m);
        var saleKey = Guid.NewGuid();
        var closeKey = Guid.NewGuid();
        var seeded = false;
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                db.Products.Add(product);
                await db.SaveChangesAsync();
                seeded = true;
            }

            var saleRequest = new
            {
                idempotencyKey = saleKey, customerType = "Visitor", paymentMethod = "Card", expectedTotal = 61m,
                items = new[] { new { productVariantId = variant.Id, quantity = 1 } }
            };
            var closeRequest = Request(closeKey, before.Date, "M0.9 race test");
            using var saleResponse = await client.PostAsJsonAsync("/api/sales", saleRequest);
            Assert.Equal(HttpStatusCode.Created, saleResponse.StatusCode);
            using var original = JsonDocument.Parse(await saleResponse.Content.ReadAsStringAsync());
            Assert.Equal("Card", original.RootElement.GetProperty("paymentMethod").GetString());
            using var restarted = new PostgreSqlCashCloseApiFactory(connectionString);
            using var restartedClient = restarted.CreateClient();
            using var retry = await restartedClient.PostAsJsonAsync("/api/sales", saleRequest);
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            using var replay = JsonDocument.Parse(await retry.Content.ReadAsStringAsync());
            Assert.Equal(original.RootElement.GetProperty("id").GetGuid(), replay.RootElement.GetProperty("id").GetGuid());
            var summary = (await restartedClient.GetFromJsonAsync<GetDailySalesSummaryResponse>("/api/sales/summary"))!;
            Assert.Equal(before.CardRevenue + 61m, summary.CardRevenue);
            Assert.Equal(before.TotalRevenue + 61m, summary.TotalRevenue);
            Assert.Equal(summary.TotalRevenue, summary.CashRevenue + summary.TransferRevenue + summary.CardRevenue);
            using var closeResponse = await restartedClient.PostAsJsonAsync("/api/cash-closes", closeRequest);
            Assert.Equal(HttpStatusCode.Created, closeResponse.StatusCode);
            var close = (await closeResponse.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
            Assert.Equal(summary.CardRevenue, close.CardSales);
            Assert.Equal(summary.TotalRevenue, close.TotalRevenue);
            Assert.Equal(close.OpeningCash + summary.CashRevenue, close.ExpectedCash);
            Assert.Equal(close.TotalRevenue, close.CashSales + close.TransferSales + close.CardSales);

        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            if (seeded)
            {
                var sales = await db.Sales.Where(sale => sale.IdempotencyKey == saleKey).ToListAsync();
                db.Sales.RemoveRange(sales);
                var rejected = await db.Set<RejectedSaleAttempt>().Where(attempt => attempt.IdempotencyKey == saleKey).ToListAsync();
                db.Set<RejectedSaleAttempt>().RemoveRange(rejected);
                var closes = await db.DailyCashCloses.Where(close => close.IdempotencyKey == closeKey).ToListAsync();
                db.DailyCashCloses.RemoveRange(closes);
                var prices = await db.ProductPrices.Where(price => price.ProductVariant.ProductId == product.Id).ToListAsync();
                db.ProductPrices.RemoveRange(prices);
                var variants = await db.ProductVariants.Where(row => row.ProductId == product.Id).ToListAsync();
                db.ProductVariants.RemoveRange(variants);
                var products = await db.Products.Where(row => row.Id == product.Id).ToListAsync();
                db.Products.RemoveRange(products);
                await db.SaveChangesAsync();
            }
        }
    }

    [PostgreSqlCashCloseFact]
    public async Task IdenticalRetryAfterApiHostRestart_ReturnsThePersistedReceipt()
    {
        var connectionString = TestConnection();
        var key = Guid.NewGuid();
        var date = new DateOnly(2002, 1, 1);
        DailyCashCloseResponse? original;
        using (var firstFactory = new PostgreSqlCashCloseApiFactory(connectionString))
        {
            using var firstClient = firstFactory.CreateClient();
            await AssertReadyAsync(firstFactory);
            date = await FindUnclosedDateAsync(firstClient, 2002);
            using var created = await firstClient.PostAsJsonAsync("/api/cash-closes", Request(key, date, "Restart test"));
            Assert.Equal(HttpStatusCode.Created, created.StatusCode);
            original = await created.Content.ReadFromJsonAsync<DailyCashCloseResponse>();
            Assert.NotNull(original);
        }

        try
        {
            using var restartedFactory = new PostgreSqlCashCloseApiFactory(connectionString);
            using var restartedClient = restartedFactory.CreateClient();
            using var retry = await restartedClient.PostAsJsonAsync("/api/cash-closes", Request(key, date, "Restart test"));
            Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
            var replay = (await retry.Content.ReadFromJsonAsync<DailyCashCloseResponse>())!;
            Assert.Equal(original!.Id, replay.Id);
            Assert.Equal(original.ClosedAtUtc, replay.ClosedAtUtc);
        }
        finally
        {
            using var cleanupFactory = new PostgreSqlCashCloseApiFactory(connectionString);
            await DeleteClosesAsync(cleanupFactory, key);
        }
    }

    private static object Request(Guid key, DateOnly date, string responsible) => new
    {
        idempotencyKey = key, businessDate = date, openingCash = 0m, countedCash = 0m,
        responsible, observations = "M0.9 PostgreSQL integration"
    };

    private static string TestConnection()
    {
        var value = Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")
            ?? throw new InvalidOperationException("MAKARI_TEST_CONNECTION is required.");
        if (new NpgsqlConnectionStringBuilder(value).Database != "makari_test")
            throw new InvalidOperationException("PostgreSQL cash close tests only permit makari_test.");
        return value;
    }

    private static async Task AssertReadyAsync(PostgreSqlCashCloseApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    private static async Task<DateOnly> FindUnclosedDateAsync(HttpClient client, int year)
    {
        for (var day = 1; day <= 28; day++)
        {
            var date = new DateOnly(year, 1, day);
            using var response = await client.GetAsync($"/api/cash-closes?date={date:yyyy-MM-dd}");
            if (response.StatusCode == HttpStatusCode.NotFound) return date;
        }
        throw new InvalidOperationException($"No unused cash close date is available in January {year} of makari_test.");
    }

    private static async Task DeleteClosesAsync(PostgreSqlCashCloseApiFactory factory, params Guid[] keys)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var closes = await db.DailyCashCloses.Where(close => keys.Contains(close.IdempotencyKey)).ToListAsync();
        db.DailyCashCloses.RemoveRange(closes);
        await db.SaveChangesAsync();
    }
}

internal sealed class PostgreSqlCashCloseFactAttribute : FactAttribute
{
    public PostgreSqlCashCloseFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")))
            Skip = "Set MAKARI_TEST_CONNECTION to a connection string whose database is makari_test after applying the migration.";
    }
}

internal sealed class PostgreSqlCashCloseApiFactory(string connectionString) : WebApplicationFactory<Program>
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
