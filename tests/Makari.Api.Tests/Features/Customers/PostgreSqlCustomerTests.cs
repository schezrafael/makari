using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Customers;
using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.CashCloses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Makari.Api.Tests.Features.Customers;

public sealed class PostgreSqlCustomerTests
{
    [PostgreSqlCustomerFact]
    public async Task ConcurrentCreationAndSaleReplay_UseOneBuyerAndPersistSnapshot()
    {
        var connection = Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")!;
        if (new NpgsqlConnectionStringBuilder(connection).Database != "makari_test")
            throw new InvalidOperationException("Customer integration tests only permit makari_test.");
        using var factory = new PostgreSqlCashCloseApiFactory(connection);
        using var client = factory.CreateClient();
        var username = $"M12-{Guid.NewGuid():N}";
        var key = Guid.NewGuid();
        var secondKey = Guid.NewGuid();
        var rejectedKey = Guid.NewGuid();
        var product = new Product(username, "Integration test");
        var variant = product.AddVariant("Bottle", 300);
        variant.AddPrice(CustomerType.Visitor, 60m);
        using (var scope = factory.Services.CreateScope())
            Assert.Empty(await scope.ServiceProvider.GetRequiredService<MakariDbContext>().Database.GetPendingMigrationsAsync());
        try
        {
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                Assert.True(await db.Customers.AnyAsync(row => row.NormalizedUsername == "visitante"));
                Assert.True(await db.Customers.AnyAsync(row => row.NormalizedUsername == "local"));
                db.Products.Add(product);
                await db.SaveChangesAsync();
            }
            var request = new { idempotencyKey = key, customerType = "Visitor", paymentMethod = "Card", expectedTotal = 60m,
                customerUsername = username, items = new[] { new { productVariantId = variant.Id, quantity = 1 } } };
            var concurrent = await Task.WhenAll(
                client.PostAsJsonAsync("/api/sales", request),
                client.PostAsJsonAsync("/api/sales", request with { idempotencyKey = secondKey, customerUsername = $"  {username.ToLowerInvariant()}  " }));
            using var sale = concurrent[0];
            using var second = concurrent[1];
            Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);
            using var firstJson = JsonDocument.Parse(await sale.Content.ReadAsStringAsync());
            using var secondJson = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            Assert.Equal(firstJson.RootElement.GetProperty("customerId").GetGuid(), secondJson.RootElement.GetProperty("customerId").GetGuid());
            using var rejected = await client.PostAsJsonAsync("/api/sales", request with { idempotencyKey = rejectedKey, customerUsername = username + "x", expectedTotal = 1m });
            Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
                Assert.Equal(1, await db.Customers.CountAsync(row => row.NormalizedUsername == username.ToLowerInvariant()));
                Assert.False(await db.Customers.AnyAsync(row => row.NormalizedUsername == username.ToLowerInvariant() + "x"));
            }
            var frequent = (await client.GetFromJsonAsync<CustomerResponse[]>("/api/customers/frequent?limit=5"))!;
            Assert.DoesNotContain(frequent, buyer => buyer.Username is "visitante" or "local");
            using var restarted = new PostgreSqlCashCloseApiFactory(connection);
            using var retryClient = restarted.CreateClient();
            using var replay = await retryClient.PostAsJsonAsync("/api/sales", request);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(await sale.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
            using var json = JsonDocument.Parse(await replay.Content.ReadAsStringAsync());
            Assert.Equal(username.ToLowerInvariant(), json.RootElement.GetProperty("customerUsernameSnapshot").GetString()!.ToLowerInvariant());
            using var conflict = await retryClient.PostAsJsonAsync("/api/sales", request with { customerUsername = "visitante" });
            Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        }
        finally
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            await db.Sales.Where(sale => sale.IdempotencyKey == key || sale.IdempotencyKey == secondKey).ExecuteDeleteAsync();
            await db.Set<Makari.Api.Features.Sales.Persistence.RejectedSaleAttempt>().Where(row => row.IdempotencyKey == key || row.IdempotencyKey == secondKey || row.IdempotencyKey == rejectedKey).ExecuteDeleteAsync();
            await db.ProductPrices.Where(price => price.ProductVariant.ProductId == product.Id).ExecuteDeleteAsync();
            await db.ProductVariants.Where(row => row.ProductId == product.Id).ExecuteDeleteAsync();
            await db.Products.Where(row => row.Id == product.Id).ExecuteDeleteAsync();
            await db.Customers.Where(row => row.NormalizedUsername == username.ToLowerInvariant()).ExecuteDeleteAsync();
        }
    }
}

internal sealed class PostgreSqlCustomerFactAttribute : FactAttribute
{
    public PostgreSqlCustomerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")))
            Skip = "Opt-in: set MAKARI_TEST_CONNECTION to makari_test with AddCustomers applied and today's business date open.";
    }
}
