using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Sales.Persistence;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.CashCloses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Makari.Api.Tests.Features.Sales.SaleConditions;

public sealed class PostgreSqlSaleConditionTests
{
    [SaleConditionsPostgreSqlFact]
    public async Task ConcurrentConditionsAndClassificationsAreAtomicAndRestartReplaysFreeSale()
    {
        var connection = Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")!;
        if (new NpgsqlConnectionStringBuilder(connection).Database != "makari_test") throw new InvalidOperationException("Only makari_test is permitted.");
        using var factory = new PostgreSqlCashCloseApiFactory(connection);
        using var otherFactory = new PostgreSqlCashCloseApiFactory(connection);
        using var client = factory.CreateClient(); using var other = otherFactory.CreateClient();
        var product = new Product($"M14-{Guid.NewGuid():N}", "Test");
        var variant = product.AddVariant("Test", 100); variant.AddPrice(CustomerType.Visitor, 60); variant.AddPrice(CustomerType.Local, 50);
        var buyer = new Customer($"M14-{Guid.NewGuid():N}");
        var request = SaleConditionsTests.NewRequest(variant.Id, "Courtesy", 0, null) with { CustomerId = buyer.Id };
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        // The test requires today's business date open and does not alter any close.
        db.Products.Add(product); db.Customers.Add(buyer); await db.SaveChangesAsync();
        try
        {
            var results = await Task.WhenAll(client.PostAsJsonAsync("/api/sales", request), other.PostAsJsonAsync("/api/sales", request with { SaleCondition = "Staff" }));
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Created);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var winner = await results.Single(x => x.StatusCode == HttpStatusCode.Created).Content.ReadFromJsonAsync<JsonElement>();
            var winningRequest = request with { SaleCondition = winner.GetProperty("saleCondition").GetString() };
            using var restarted = new PostgreSqlCashCloseApiFactory(connection);
            using var retry = restarted.CreateClient();
            var replay = await retry.PostAsJsonAsync("/api/sales", winningRequest);
            Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
            Assert.Equal(0, (await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("total").GetDecimal());
            Assert.Equal(1, await db.Sales.CountAsync(s => s.IdempotencyKey == request.IdempotencyKey));
            var updates = await Task.WhenAll(
                client.PutAsJsonAsync($"/api/customers/{buyer.Id}/classification", new { customerType = "Local", expectedCustomerType = "Visitor" }),
                other.PutAsJsonAsync($"/api/customers/{buyer.Id}/classification", new { customerType = "Local", expectedCustomerType = "Visitor" }));
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.NoContent);
            Assert.Single(updates, x => x.StatusCode == HttpStatusCode.Conflict);
            // Exercise PostgreSQL aggregate translation, including free units and conditions.
            var summary = await client.GetAsync("/api/sales/summary");
            Assert.Equal(HttpStatusCode.OK, summary.StatusCode);
            Assert.True((await summary.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("freeUnits").GetInt64() >= 2);
        }
        finally
        {
            await db.Sales.Where(x => x.IdempotencyKey == request.IdempotencyKey).ExecuteDeleteAsync();
            await db.Set<RejectedSaleAttempt>().Where(x => x.IdempotencyKey == request.IdempotencyKey).ExecuteDeleteAsync();
            await db.ProductPrices.Where(x => x.ProductVariantId == variant.Id).ExecuteDeleteAsync();
            await db.ProductVariants.Where(x => x.Id == variant.Id).ExecuteDeleteAsync();
            await db.Products.Where(x => x.Id == product.Id).ExecuteDeleteAsync();
            await db.Customers.Where(x => x.Id == buyer.Id).ExecuteDeleteAsync();
        }
    }
}
internal sealed class SaleConditionsPostgreSqlFactAttribute : FactAttribute
{
    public SaleConditionsPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")))
            Skip = "Opt-in makari_test only: manually apply CustomerClassificationAndSaleConditions and leave today's business date open.";
    }
}
