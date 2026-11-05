using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Makari.Api.Tests.Features.Catalog;

public sealed class CatalogMigrationTests
{
    [Fact]
    public void ManagementMigration_AddsConcurrencyAndNonNegativeChecksWithoutChangingAmounts()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261105050434_SimplifyCustomerIdentification");
        Assert.Contains("ADD \"Version\" uuid NOT NULL", sql);
        Assert.Contains("CHECK (\"Amount\" >= 0)", sql);
        Assert.Contains("CHECK (\"UnitPrice\" >= 0)", sql);
        Assert.DoesNotContain("UPDATE ", sql);
        Assert.DoesNotContain("DELETE ", sql);
        Assert.DoesNotContain("INSERT INTO \"Sales\"", sql);
    }

    [Fact]
    public void Card_MigrationPreservesHistoricalSnapshotsAndPaymentValues()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261027035313_AddDailyCashCloses", "20261105002201_AddCardPaymentMethod");
        Assert.Contains("ADD \"CardSales\" numeric(28,2) NOT NULL DEFAULT 0.0", sql);
        Assert.Contains("CHECK (\"PaymentMethod\" IN (0, 1, 2))", sql);
        Assert.DoesNotContain("UPDATE ", sql);
        Assert.DoesNotContain("DELETE ", sql);
        Assert.DoesNotContain("DROP COLUMN", sql);
    }

    [Fact]
    public void RejectedAttempts_MigrationIsSchemaOnlyAndMatchesModel()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.Contains(context.Database.GetMigrations(), migration => migration.EndsWith("_AddRejectedSaleAttempts"));
        Assert.False(context.Database.HasPendingModelChanges());
        var sql = context.GetService<IMigrator>().GenerateScript("20261020220102_AddSales");
        Assert.Contains("CREATE TABLE \"RejectedSaleAttempts\"", sql);
        Assert.DoesNotContain("UPDATE \"ProductPrices\"", sql);
        Assert.DoesNotContain("INSERT INTO \"Sales\"", sql);
    }

    [Fact]
    public void DailyCashCloses_MigrationAddsImmutableSnapshotSchemaOnly()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.Contains("20261027035313_AddDailyCashCloses", context.Database.GetMigrations());
        Assert.False(context.Database.HasPendingModelChanges());

        var sql = context.GetService<IMigrator>().GenerateScript(
            "20261024021902_AddRejectedSaleAttempts", "20261027035313_AddDailyCashCloses");
        Assert.Contains("CREATE TABLE \"DailyCashCloses\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_DailyCashCloses_BusinessDate\"", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_DailyCashCloses_IdempotencyKey\"", sql);
        Assert.Contains("ADD \"ConflictBusinessDate\" date", sql);
        Assert.Contains("ADD \"ConflictCode\" character varying(64)", sql);
        Assert.DoesNotContain("UPDATE \"ProductPrices\"", sql);
        Assert.DoesNotContain("INSERT INTO \"Sales\"", sql);
    }

    [Fact]
    public void AddSales_MatchesCurrentModelAndContainsExpectedSalesSchema()
    {
        using var context = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);

        Assert.Contains("20261020220102_AddSales", context.Database.GetMigrations());
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
