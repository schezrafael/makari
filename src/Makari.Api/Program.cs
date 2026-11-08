using Makari.Api.Infrastructure.Hosting;
using Makari.Api.Features.Catalog.ListProducts;
using Makari.Api.Features.Catalog.CreateProduct;
using Makari.Api.Features.Catalog.UpdateProduct;
using Makari.Api.Features.Catalog.CreateProductVariant;
using Makari.Api.Features.Catalog.UpdateProductVariant;
using Makari.Api.Features.Catalog.UpdateProductPrice;
using Makari.Api.Features.Customers;
using Makari.Api.Features.Catalog.GetCatalog;
using Makari.Api.Features.Catalog.Persistence;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Makari.Api.Features.CashCloses.GetDailyCashClose;
using Makari.Api.Features.CashCloses.GetDailyCashCloseById;
using Makari.Api.Features.Health;
using Makari.Api.Features.Sales.CreateSale;
using Makari.Api.Features.Sales.GetDailySalesSummary;
using Makari.Api.Features.Sales.QuoteSale;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Railway supplies PORT at runtime. Other hosts retain their usual URL configuration.
if (builder.Configuration["PORT"] is { } portValue)
{
    if (!int.TryParse(portValue, out var port) || port is < 1 or > 65535)
        throw new InvalidOperationException("PORT must be an integer between 1 and 65535.");
    builder.WebHost.UseUrls($"http://*:{port}");
}
builder.Services.Configure<RouteOptions>(options =>
    options.ConstraintMap["spa"] = typeof(SpaRouteConstraint));

builder.Services.AddDbContext<MakariDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Makari")
        ?? builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<GetCatalogHandler>();
builder.Services.AddScoped<CreateSaleHandler>();
builder.Services.AddScoped<QuoteSaleHandler>();
builder.Services.AddScoped<GetDailySalesSummaryHandler>();
builder.Services.AddScoped<CreateDailyCashCloseHandler>();
builder.Services.AddScoped<GetDailyCashCloseHandler>();
builder.Services.AddScoped<GetDailyCashCloseByIdHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

// OpenAPI
builder.Services.AddOpenApi();

var app = builder.Build();

// Explicit maintenance command; normal API startup never migrates or seeds.
if (args.Contains("--seed-catalog", StringComparer.Ordinal))
{
    await using var scope = app.Services.CreateAsyncScope();
    var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
    await CatalogSeeder.SeedAsync(context);
    Console.WriteLine("Catalog initialization completed.");
    return;
}

// Files are copied into wwwroot by the final Docker stage. Use runtime file serving
// rather than a build-time static assets manifest. Never serve files under /api.
app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)
    && !context.Request.Path.StartsWithSegments("/health", StringComparison.OrdinalIgnoreCase),
    web => web.UseStaticFiles());

// API documentation (development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapListProductsEndpoint();
app.MapCreateProductEndpoint();
app.MapUpdateProductEndpoint();
app.MapCreateProductVariantEndpoint();
app.MapUpdateProductVariantEndpoint();
app.MapUpdateProductPriceEndpoint();
app.MapCustomerEndpoints();
app.MapHealthEndpoint();
app.MapGetCatalogEndpoint();
app.MapCreateSaleEndpoint();
app.MapQuoteSaleEndpoint();
app.MapGetDailySalesSummaryEndpoint();
app.MapCreateDailyCashCloseEndpoint();
app.MapGetDailyCashCloseEndpoint();
app.MapGetDailyCashCloseByIdEndpoint();
app.MapFallbackToFile("{*path:spa:nonfile}", "index.html");
app.Run();

// Allows the integration tests to start the application in memory.
public partial class Program;
