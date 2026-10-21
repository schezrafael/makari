using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogMigrationTests
{
    [Fact]
    public void AddSales_MatchesCurrentModelAndContainsExpectedSalesSchema()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);

        Assert.EndsWith("_AddSales", context.Database.GetMigrations().Last());
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261020170441_InitialCatalog", "20261020220102_AddSales");

        Assert.Contains("CREATE TABLE \"Sales\"", sql);
        Assert.Contains("CREATE TABLE \"SaleItems\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Sales_IdempotencyKey\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_SaleItems_SaleId_ProductVariantId\"", sql);
        Assert.Contains("numeric(28,2)", sql);
        Assert.Contains("FK_SaleItems_ProductVariants_ProductVariantId", sql);
        Assert.Contains("FK_SaleItems_Sales_SaleId", sql);
        Assert.DoesNotContain("INSERT INTO \"Sales\"", sql);
        Assert.DoesNotContain("INSERT INTO \"SaleItems\"", sql);
    }

    [Fact]
    public void InitialCatalog_MatchesCurrentModelAndContainsOnlySchemaChanges()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);

        Assert.EndsWith("_InitialCatalog", context.Database.GetMigrations().First());
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
