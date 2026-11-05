using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Tests.Features.Catalog.GetCatalog;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogManagementTests
{
    internal static async Task<ProductResponse> Create(HttpClient client, string? name = null)
    {
        var response = await client.PostAsJsonAsync("/api/products", new { name = name ?? "Bebida", category = "Bebidas" });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
    internal static async Task<ProductResponse> Variant(HttpClient client, ProductResponse p)
    {
        var response = await client.PostAsJsonAsync($"/api/products/{p.Id}/variants", new { name = "Natural", sizeMl = 350, visitorPrice = 60, localPrice = 50, p.Version });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }
    private static async Task<ProductResponse> Read(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    [Fact]
    public async Task ManageProductsVariantsAndPrices_WithVersionedConflictsAndActiveCatalog()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var p = await Variant(client, await Create(client));
        var path = $"/api/products/{p.Id}";
        var stale = p.Version;
        p = await Read(await client.PutAsJsonAsync(path, new { name = "Renombrada", isActive = false, p.Version }));
        Assert.False(p.IsActive);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/sales", new { customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 60, idempotencyKey = Guid.NewGuid(), items = new[] { new { productVariantId = p.Variants[0].Id, quantity = 1 } } })).StatusCode);
        Assert.Equal("Renombrada", p.Name);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync(path, new { name = "Perdida", isActive = true, version = stale })).StatusCode);
        Assert.Contains((await client.GetFromJsonAsync<ProductResponse[]>("/api/products"))!, x => !x.IsActive);
        Assert.DoesNotContain("Renombrada", await client.GetStringAsync("/api/catalog"));
        p = await Read(await client.PutAsJsonAsync(path, new { p.Name, isActive = true, p.Version }));
        var vpath = $"{path}/variants/{p.Variants[0].Id}";
        p = await Read(await client.PutAsJsonAsync(vpath, new { name = "Nueva", sizeMl = 500, isActive = false, p.Version }));
        Assert.Equal(500, p.Variants[0].SizeMl);
        Assert.False(p.Variants[0].IsActive);
        p = await Read(await client.PutAsJsonAsync(vpath, new { name = "Nueva", sizeMl = 500, isActive = true, p.Version }));
        Assert.Contains("Renombrada", await client.GetStringAsync("/api/catalog"));
        p = await Read(await client.PutAsJsonAsync(vpath + "/prices", new { visitorPrice = 0, localPrice = 0, p.Version }));
        Assert.Equal(0, p.Variants[0].VisitorPrice);
        var sale = await client.PostAsJsonAsync("/api/sales", new { customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 0, idempotencyKey = Guid.NewGuid(), items = new[] { new { productVariantId = p.Variants[0].Id, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
    }

    [Fact]
    public async Task PriceChangesPreserveHistoryReplayAndRequireNewConfirmation()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var p = await Variant(client, await Create(client));
        var items = new[] { new { productVariantId = p.Variants[0].Id, quantity = 1 } };
        var request = new { customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 60, idempotencyKey = Guid.NewGuid(), items };
        var sale = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(HttpStatusCode.Created, sale.StatusCode);
        var vpath = $"/api/products/{p.Id}/variants/{p.Variants[0].Id}";
        p = await Read(await client.PutAsJsonAsync(vpath + "/prices", new { visitorPrice = 75, localPrice = 65, p.Version }));
        var replay = await client.PostAsJsonAsync("/api/sales", request);
        Assert.Equal(await sale.Content.ReadAsStringAsync(), await replay.Content.ReadAsStringAsync());
        var pending = request with { idempotencyKey = Guid.NewGuid() };
        var conflict = await client.PostAsJsonAsync("/api/sales", pending);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Contains("price_changed", await conflict.Content.ReadAsStringAsync());
        var quote = await client.PostAsJsonAsync("/api/sales/quote", new { request.customerType, items });
        var json = await quote.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(75, json.GetProperty("total").GetDecimal());
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/sales", pending with { expectedTotal = 75 })).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/sales", pending with { expectedTotal = 75, idempotencyKey = Guid.NewGuid() })).StatusCode);
        p = await Read(await client.PutAsJsonAsync(vpath, new { name = "Natural", sizeMl = 350, isActive = false, p.Version }));
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/sales", pending with { expectedTotal = 75, idempotencyKey = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/sales", request)).StatusCode);
    }

    [Fact]
    public async Task StaleEfWriterCannotOverwriteNewerProductVersion()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        var p = await Create(client);
        using var firstScope = factory.Services.CreateScope();
        using var staleScope = factory.Services.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var staleDb = staleScope.ServiceProvider.GetRequiredService<MakariDbContext>();
        var first = await firstDb.Products.SingleAsync(x => x.Id == p.Id);
        var stale = await staleDb.Products.SingleAsync(x => x.Id == p.Id);
        first.Update("Ganadora", true);
        first.Revise();
        await firstDb.SaveChangesAsync();
        stale.Update("Perdida", false);
        stale.Revise();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => staleDb.SaveChangesAsync());
        var current = Assert.Single((await client.GetFromJsonAsync<ProductResponse[]>("/api/products"))!);
        Assert.Equal("Ganadora", current.Name);
        Assert.True(current.IsActive);
    }

    [Fact]
    public async Task ValidatesNamesPricesIdsAndDuplicates()
    {
        using var factory = new CatalogApiFactory();
        await factory.InitializeAsync();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", new { name = "  ", category = "Bebidas" })).StatusCode);
        var p = await Variant(client, await Create(client));
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/products", new { name = " bebida ", category = "Bebidas" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/products/{p.Id}/variants", new { name = " natural ", sizeMl = 350, visitorPrice = 60, localPrice = 50, p.Version })).StatusCode);
        foreach (var price in new[] { -1m, 0.001m, 10_000_000_000m })
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/api/products/{p.Id}/variants/{p.Variants[0].Id}/prices", new { visitorPrice = price, localPrice = 50, p.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/products/{Guid.NewGuid()}", new { name = "Otra", isActive = true, p.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync($"/api/products/{p.Id}/variants/{Guid.NewGuid()}", new { name = "Otra", sizeMl = 100, isActive = true, p.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/products/{p.Id}", new { name = "Otra", isActive = true })).StatusCode);
    }
}
