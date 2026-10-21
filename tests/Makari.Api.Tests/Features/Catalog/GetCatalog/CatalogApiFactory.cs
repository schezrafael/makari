using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Makari.Api.Tests.Features.Catalog.GetCatalog;

internal sealed class CatalogApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public CatalogApiFactory() => _connection.Open();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<MakariDbContext>();
            services.RemoveAll<DbContextOptions<MakariDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<MakariDbContext>>();
            services.AddDbContext<MakariDbContext>(options => options.UseSqlite(_connection));
        });
    }

    public async Task InitializeAsync(params Product[] products)
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        // This schema and data exist only in this factory's isolated memory connection.
        await context.Database.EnsureCreatedAsync();
        context.Products.AddRange(products);
        await context.SaveChangesAsync();
    }

    public async Task InstallSaleItemFailureAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MakariDbContext>();
        await context.Database.EnsureCreatedAsync();
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER RejectSaleItems BEFORE INSERT ON SaleItems
            BEGIN SELECT RAISE(ABORT, 'forced atomicity test failure'); END;
            """);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
