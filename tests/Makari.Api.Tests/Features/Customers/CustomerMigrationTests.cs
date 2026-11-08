using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Makari.Api.Tests.Features.Customers;

public sealed class CustomerMigrationTests
{
    [Fact]
    public void Simplification_PreservesSalesAndHandlesExistingReservedNames()
    {
        using var db = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261105040101_AddCustomers", "20261105050434_SimplifyCustomerIdentification");
        Assert.Contains("NOT EXISTS", sql);
        Assert.Contains("'visitante'", sql);
        Assert.Contains("'local'", sql);
        Assert.DoesNotContain("UPDATE \"Sales\"", sql);
        Assert.DoesNotContain("DELETE FROM", sql);
        Assert.DoesNotContain("DROP TABLE", sql);
    }

    [Fact]
    public void AddCustomers_PreservesHistoricalSalesAndPrices_AndReservesGenericBuyers()
    {
        using var db = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>()
            .UseNpgsql("Host=localhost;Database=makari_migration_unit_tests").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var sql = db.GetService<IMigrator>().GenerateScript("20261105002201_AddCardPaymentMethod", "20261105040101_AddCustomers");
        Assert.Contains("CREATE TABLE \"Customers\"", sql);
        Assert.Contains("GENERATED ALWAYS AS (lower(\"Username\")) STORED", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_Customers_NormalizedUsername\"", sql);
        Assert.Contains("ADD \"CustomerId\" uuid", sql);
        Assert.Contains("ADD \"CustomerUsernameSnapshot\" character varying(40)", sql);
        Assert.Contains("ON DELETE RESTRICT", sql);
        Assert.Contains("'v'", sql);
        Assert.Contains("'L'", sql);
        Assert.DoesNotContain("UPDATE ", sql);
        Assert.DoesNotContain("DELETE ", sql.Replace("ON DELETE RESTRICT", ""));
        Assert.DoesNotContain("INSERT INTO \"Sales\"", sql);
        Assert.DoesNotContain("ProductPrices", sql);
    }
}
