using Makari.Api.Features.Catalog.Persistence;
using Makari.Api.Features.Health;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<MakariDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Makari")
        ?? builder.Configuration.GetConnectionString("DefaultConnection")));

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

app.MapHealthEndpoint();
app.Run();

// Allows the integration tests to start the application in memory.
public partial class Program;
