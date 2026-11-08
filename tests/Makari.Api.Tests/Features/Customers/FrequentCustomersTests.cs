using System.Net;
using System.Net.Http.Json;
using Makari.Api.Features.Customers;
using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Customers;

public sealed class FrequentCustomersTests
{
    [Fact]
    public async Task Frequent_OrdersBySalesThenRecency_ExcludesGenericsRejectedAndHistoricalNulls()
    {
        using var factory = new CatalogApiFactory();
        var product = new Product("Frequency test", "Test");
        var variant = product.AddVariant("Bottle", 300);
        variant.AddPrice(CustomerType.Visitor, 60m);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<CustomerResponse[]>("/api/customers/frequent"))!);
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
            var buyers = Enumerable.Range(0, 7).Select(i => new Customer($"Buyer{i}")).ToArray();
            db.Customers.AddRange(buyers);
            await db.SaveChangesAsync();
            var date = DateTimeOffset.UtcNow.AddDays(-10);
            void Add(Guid? id, string? name, int count, DateTimeOffset at)
            {
                for (var i = 0; i < count; i++)
                {
                    var sale = new Sale(CustomerType.Visitor, PaymentMethod.Cash, "Test", Guid.NewGuid(), "test", at, id, name);
                    sale.AddItem(variant.Id, 1, 60m);
                    db.Sales.Add(sale);
                }
            }
            for (var i = 0; i < 7; i++) Add(buyers[i].Id, buyers[i].Username, i == 0 ? 3 : 2, date.AddHours(i));
            Add(Customer.VisitorId, "visitante", 10, date);
            Add(Customer.LocalId, "local", 10, date);
            Add(null, null, 10, date);
            await db.SaveChangesAsync();
        }
        using var rejected = await client.PostAsJsonAsync("/api/sales", new { idempotencyKey = Guid.NewGuid(), customerType = "Visitor", paymentMethod = "Cash", expectedTotal = 1m, customerUsername = "Never sold", items = new[] { new { productVariantId = variant.Id, quantity = 1 } } });
        Assert.Equal(HttpStatusCode.Conflict, rejected.StatusCode);
        var result = (await client.GetFromJsonAsync<CustomerResponse[]>("/api/customers/frequent?limit=5"))!;
        Assert.Equal(new[] { "Buyer0", "Buyer6", "Buyer5", "Buyer4", "Buyer3" }, result.Select(row => row.Username));
        Assert.Single((await client.GetFromJsonAsync<CustomerResponse[]>("/api/customers/frequent?limit=1"))!);
        foreach (var limit in new[] { "0", "6", "bad" })
        {
            using var invalid = await client.GetAsync($"/api/customers/frequent?limit={limit}");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var search = await client.GetAsync("/api/customers?username=Buyer0");
        Assert.Equal(HttpStatusCode.OK, search.StatusCode);
        using var create = await client.PostAsJsonAsync("/api/customers", new { username = "Other" });
        Assert.Equal(HttpStatusCode.MethodNotAllowed, create.StatusCode);
    }
}
