using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogMigrationTests
{
    [Fact]
    public void InitialCatalog_MatchesCurrentModelAndContainsOnlySchemaChanges()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);

        Assert.EndsWith("_InitialCatalog", Assert.Single(context.Database.GetMigrations()));
        Assert.False(context.Database.HasPendingModelChanges());

        var sql = context.GetService<IMigrator>().GenerateScript();
        Assert.Contains("CREATE TABLE \"Products\"", sql);
        Assert.Contains("CREATE TABLE \"ProductVariants\"", sql);
        Assert.Contains("CREATE TABLE \"ProductPrices\"", sql);
        Assert.Contains("\"Amount\" numeric(12,2) NOT NULL", sql);
        Assert.Contains("CHECK (\"Amount\" > 0)", sql);
        Assert.Contains("CHECK (\"SizeMl\" > 0)", sql);
        Assert.Contains("CHECK (\"CustomerType\" IN (0, 1))", sql);
        Assert.Contains("CREATE UNIQUE INDEX", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.DoesNotContain("INSERT INTO \"Products\"", sql);
        Assert.DoesNotContain("INSERT INTO \"ProductPrices\"", sql);
    }
}
