using Makari.Api.Features.Catalog.GetCatalog;
using Makari.Api.Features.Catalog.Persistence;
using Makari.Api.Features.Health;
using Makari.Api.Features.Sales.CreateSale;
using Makari.Api.Features.Sales.QuoteSale;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MakariDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Makari")
        ?? builder.Configuration.GetConnectionString("DefaultConnection")));
builder.Services.AddScoped<GetCatalogHandler>();
builder.Services.AddScoped<CreateSaleHandler>();
builder.Services.AddScoped<QuoteSaleHandler>();
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));

// Features
builder.Services.AddScoped<GetCatalogHandler>();

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

// API documentation (development only)
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapHealthEndpoint();
app.MapGetCatalogEndpoint();
app.MapCreateSaleEndpoint();
app.MapQuoteSaleEndpoint();
app.Run();

// Allows the integration tests to start the application in memory.
public partial class Program;
