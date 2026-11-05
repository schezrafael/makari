using System.Net;
using System.Net.Http.Json;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.CashCloses;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class PostgreSqlCatalogManagementTests
{
    [CatalogPostgreSqlFact]
    public async Task ConcurrentUpdatesAndDuplicateCreates_OnlyOneWins()
    {
        var connection = Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")!;
        if (new NpgsqlConnectionStringBuilder(connection).Database != "makari_test")
            throw new InvalidOperationException("Catalog tests only permit makari_test.");
        using var factory = new PostgreSqlCashCloseApiFactory(connection);
        using var client = factory.CreateClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var name = $"M13-{Guid.NewGuid():N}";
        var p = await CatalogManagementTests.Create(client, name);
        try
        {
            p = await CatalogManagementTests.Variant(client, p);
            var path = $"/api/products/{p.Id}/variants/{p.Variants[0].Id}/prices";
            var results = await Task.WhenAll(
                client.PutAsJsonAsync(path, new { visitorPrice = 20, localPrice = 10, p.Version }),
                client.PutAsJsonAsync(path, new { visitorPrice = 30, localPrice = 15, p.Version }));
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(results, r => r.StatusCode == HttpStatusCode.Conflict);
            var duplicates = await Task.WhenAll(
                client.PostAsJsonAsync("/api/products", new { name = name + "-duplicate", category = "Test" }),
                client.PostAsJsonAsync("/api/products", new { name = name + "-duplicate", category = "Test" }));
            Assert.Single(duplicates, r => r.StatusCode == HttpStatusCode.Created);
            Assert.Single(duplicates, r => r.StatusCode == HttpStatusCode.Conflict);
        }
        finally
        {
            // Clean up only data created by this opt-in test, never development data.
            await db.ProductPrices.Where(x => x.ProductVariant.ProductId == p.Id).ExecuteDeleteAsync();
            await db.ProductVariants.Where(x => x.ProductId == p.Id).ExecuteDeleteAsync();
            await db.Products.Where(x => x.Id == p.Id || x.Name == name + "-duplicate").ExecuteDeleteAsync();
        }
    }
}

internal sealed class CatalogPostgreSqlFactAttribute : FactAttribute
{
    public CatalogPostgreSqlFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAKARI_TEST_CONNECTION")))
            Skip = "Opt-in: MAKARI_TEST_CONNECTION must target makari_test with ManageCatalog manually applied.";
    }
}
