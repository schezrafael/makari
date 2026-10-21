using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.CreateSale;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Tests.Features.Catalog.GetCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Makari.Api.Tests.Features.Sales.CreateSale;

public sealed class CreateSaleTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    [Fact]
    public async Task CreateSale_ValidSaleUsesCatalogPriceAndConfiguredOperator()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 2);

        using var response = await PostAsync(client, request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var sale = (await response.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;
        Assert.Equal(CustomerType.Visitor, sale.CustomerType);
        Assert.Equal(PaymentMethod.Cash, sale.PaymentMethod);
        Assert.Equal(120m, sale.Total);
        Assert.Equal("Andi", sale.RegisteredBy);
        Assert.Equal(request.IdempotencyKey, sale.IdempotencyKey);
        Assert.Single(sale.Items);
        Assert.Equal(natural.Id, sale.Items[0].ProductVariantId);
        Assert.Equal(60m, sale.Items[0].UnitPrice);
        Assert.Equal(120m, sale.Items[0].Subtotal);
    }

    [Fact]
    public async Task CreateSale_MultipleItemsPersistsHistoricalPriceAndCorrectTotal()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out var citric);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 2) with
        {
            Items = [new(natural.Id, 2), new(citric.Id, 1)]
        };

        using var response = await PostAsync(client, request);
        var sale = (await response.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(195m, sale.Total);
        Assert.Equal(2, sale.Items.Count);
        Assert.Equal(120m, sale.Items.Single(item => item.ProductVariantId == natural.Id).Subtotal);
        Assert.Equal(75m, sale.Items.Single(item => item.ProductVariantId == citric.Id).Subtotal);

        using var scope = factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        Assert.Equal(1, await context.Sales.CountAsync());
        Assert.Equal(2, await context.SaleItems.CountAsync());
        await context.Database.ExecuteSqlInterpolatedAsync($"UPDATE ProductPrices SET Amount = 99 WHERE ProductVariantId = {natural.Id}");
        var stored = await context.SaleItems.AsNoTracking().SingleAsync(item => item.ProductVariantId == natural.Id);
        Assert.Equal(60m, stored.UnitPrice);
        Assert.Equal(120m, stored.Subtotal);
    }

    [Theory]
    [InlineData(CustomerType.Local, 50)]
    [InlineData(CustomerType.Visitor, 60)]
    public async Task CreateSale_UsesPriceForRequestedCustomerType(CustomerType customerType, decimal expected)
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 1) with { CustomerType = customerType };

        using var response = await PostAsync(client, request);
        var sale = (await response.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;

        Assert.Equal(expected, sale.Items[0].UnitPrice);
        Assert.Equal(expected, sale.Total);
    }

    [Fact]
    public async Task CreateSale_RejectsUnknownOrInactiveVariantsAndMissingPrices()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out _, out _);
        var inactive = product.AddVariant("Inactive", 500);
        inactive.AddPrice(CustomerType.Visitor, 10m);
        inactive.Deactivate();
        var withoutPrice = product.AddVariant("Unpriced", 500);
        withoutPrice.AddPrice(CustomerType.Local, 20m);
        var inactiveProduct = ProductWithPrices(out var variantOfInactive, out _);
        inactiveProduct.Deactivate();
        await factory.InitializeAsync(product, inactiveProduct);
        using var client = factory.CreateClient();

        foreach (var variantId in new[] { Guid.NewGuid(), inactive.Id, withoutPrice.Id, variantOfInactive.Id })
        {
            using var response = await PostAsync(client, NewRequest(variantId, 1));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            using var scope = factory.Services.CreateScope();
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<MakariDbContext>().Sales.CountAsync());
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task CreateSale_RejectsNonPositiveQuantity(int quantity)
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        using var response = await PostAsync(client, NewRequest(natural.Id, quantity));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await SalesCountAsync(factory));
    }

    [Fact]
    public async Task CreateSale_RejectsDuplicateVariantsAndEmptyItems()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();

        using var duplicate = await PostAsync(client, NewRequest(natural.Id, 1) with
        {
            Items = [new(natural.Id, 1), new(natural.Id, 2)]
        });
        using var empty = await PostAsync(client, NewRequest(natural.Id, 1) with { Items = [] });

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
        Assert.Equal(0, await SalesCountAsync(factory));
    }

    [Fact]
    public Task CreateSale_RejectsUndefinedEnumsAndEmptyIdempotencyKey()
    {
        var errors = CreateSaleValidator.Validate(new CreateSaleRequest(
            Guid.Empty, (CustomerType)99, (PaymentMethod)99, [new(Guid.NewGuid(), 1)]));

        Assert.Contains("idempotencyKey", errors.Keys);
        Assert.Contains("customerType", errors.Keys);
        Assert.Contains("paymentMethod", errors.Keys);
        return Task.CompletedTask;
    }

    [Theory]
    [InlineData("customerType", "Other")]
    [InlineData("paymentMethod", "Card")]
    public async Task CreateSale_InvalidEnumNamesReturnBadRequest(string property, string value)
    {
        using var factory = new CatalogApiFactory();
        using var client = factory.CreateClient();
        var customerType = property == "customerType" ? value : "Visitor";
        var paymentMethod = property == "paymentMethod" ? value : "Cash";
        var json = $$"""{"idempotencyKey":"{{Guid.NewGuid()}}","customerType":"{{customerType}}","paymentMethod":"{{paymentMethod}}","items":[{"productVariantId":"{{Guid.NewGuid()}}","quantity":1}]}""";

        using var response = await client.PostAsync("/api/sales", new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateSale_IgnoresClientPricesTotalsAndRegisteredBy()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var key = Guid.NewGuid();
        var json = $$"""{"idempotencyKey":"{{key}}","customerType":"Visitor","paymentMethod":"Transfer","registeredBy":"Mallory","total":0.01,"items":[{"productVariantId":"{{natural.Id}}","quantity":1,"unitPrice":0.01,"subtotal":0.01}]}""";

        using var response = await client.PostAsync("/api/sales", new StringContent(json, Encoding.UTF8, "application/json"));
        var sale = (await response.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(60m, sale.Total);
        Assert.Equal(60m, Assert.Single(sale.Items).UnitPrice);
        Assert.Equal("Andi", sale.RegisteredBy);
    }

    [Fact]
    public async Task CreateSale_IdenticalRetryReturnsOriginalSale()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 2);

        using var first = await PostAsync(client, request);
        using var retry = await PostAsync(client, request);
        var original = (await first.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;
        var repeated = (await retry.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions))!;

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(original.Id, repeated.Id);
        Assert.Equal(original.CreatedAtUtc, repeated.CreatedAtUtc);
        Assert.Equal(original.Items, repeated.Items);
        Assert.Equal(1, await SalesCountAsync(factory));
    }

    [Fact]
    public async Task CreateSale_EquivalentItemsInDifferentOrderAreAnIdenticalRetry()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out var citric);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 2) with { Items = [new(natural.Id, 2), new(citric.Id, 1)] };

        using var first = await PostAsync(client, request);
        using var retry = await PostAsync(client, request with { Items = request.Items.Reverse().ToArray() });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(1, await SalesCountAsync(factory));
    }

    [Fact]
    public async Task CreateSale_ReusedKeyWithDifferentContentsReturnsConflict()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 1);

        using var first = await PostAsync(client, request);
        using var retry = await PostAsync(client, request with { Items = [new(natural.Id, 2)] });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(1, await SalesCountAsync(factory));
    }

    [Theory]
    [InlineData("customerType")]
    [InlineData("paymentMethod")]
    public async Task CreateSale_ReusedKeyWithDifferentSaleOptionsReturnsConflict(string field)
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 1);
        using var first = await PostAsync(client, request);
        var changed = field == "customerType"
            ? request with { CustomerType = CustomerType.Local }
            : request with { PaymentMethod = PaymentMethod.Transfer };

        using var retry = await PostAsync(client, changed);

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal(1, await SalesCountAsync(factory));
    }

    [Fact]
    public async Task CreateSale_ConcurrentRetriesCreateOnlyOneSale()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        using var client = factory.CreateClient();
        var request = NewRequest(natural.Id, 1);

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => PostAsync(client, request)));

        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.Created);
        Assert.All(responses, response => Assert.Contains(response.StatusCode, new[] { HttpStatusCode.Created, HttpStatusCode.OK }));
        var sales = await Task.WhenAll(responses.Select(response => response.Content.ReadFromJsonAsync<CreateSaleResponse>(JsonOptions)));
        Assert.Single(sales.Select(sale => sale!.Id).Distinct());
        Assert.Equal(1, await SalesCountAsync(factory));
        foreach (var response in responses) response.Dispose();
    }

    [Fact]
    public async Task CreateSale_FailedSaleItemInsertRollsBackSaleAtomically()
    {
        using var factory = new CatalogApiFactory();
        var product = ProductWithPrices(out var natural, out _);
        await factory.InitializeAsync(product);
        await factory.InstallSaleItemFailureAsync();
        using var scope = factory.Services.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<CreateSaleHandler>();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(NewRequest(natural.Id, 1)));

        Assert.Equal(0, await context.Sales.AsNoTracking().CountAsync());
        Assert.Equal(0, await context.SaleItems.AsNoTracking().CountAsync());
    }

    private static CreateSaleRequest NewRequest(Guid variantId, int quantity) => new(
        Guid.NewGuid(), CustomerType.Visitor, PaymentMethod.Cash, [new(variantId, quantity)]);

    private static Product ProductWithPrices(out ProductVariant natural, out ProductVariant citric)
    {
        var product = new Product("Cold Brew", "Café");
        natural = product.AddVariant("Natural", 500);
        natural.AddPrice(CustomerType.Visitor, 60m);
        natural.AddPrice(CustomerType.Local, 50m);
        citric = product.AddVariant("Cítrico", 500);
        citric.AddPrice(CustomerType.Visitor, 75m);
        citric.AddPrice(CustomerType.Local, 60m);
        return product;
    }

    private static async Task<int> SalesCountAsync(CatalogApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MakariDbContext>().Sales.CountAsync();
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, CreateSaleRequest request) =>
        client.PostAsJsonAsync("/api/sales", request, JsonOptions);

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }
}
