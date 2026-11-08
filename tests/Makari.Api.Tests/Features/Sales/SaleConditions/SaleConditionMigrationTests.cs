using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Data.Sqlite;

namespace Makari.Api.Tests.Features.Sales.SaleConditions;

public sealed class SaleConditionMigrationTests
{
    [Fact]
    public void MigrationBackfillsConditionsWithoutRewritingMoneyFingerprintsOrCloses()
    {
        using var db = new MakariDbContext(new DbContextOptionsBuilder<MakariDbContext>().UseNpgsql("Host=localhost;Database=makari_test").Options);
        Assert.False(db.Database.HasPendingModelChanges());
        var migration = db.GetService<IMigrationsAssembly>().CreateMigration(
            db.GetService<IMigrationsAssembly>().Migrations.Single(x => x.Key.EndsWith("_CustomerClassificationAndSaleConditions")).Value, db.Database.ProviderName!);
        var sql = Assert.Single(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.SqlOperation>()).Sql;
        Assert.Contains("CASE \"CustomerType\" WHEN 0 THEN 1 ELSE 0 END", sql);
        var script = db.GetService<IMigrator>().GenerateScript("20261105053626_ManageCatalog");
        Assert.DoesNotContain("UPDATE \"SaleItems\"", script);
        Assert.DoesNotContain("UPDATE \"DailyCashCloses\"", script);
        Assert.DoesNotContain("UPDATE \"RejectedSaleAttempts\"", script);
        Assert.DoesNotContain("DROP COLUMN \"CustomerType\"", script);
        Assert.DoesNotContain("SET \"Total\"", script);
        // Execute the actual backfill statement against isolated representative historical rows.
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE Sales (CustomerType integer, SaleCondition integer DEFAULT 0, Total numeric, PaymentMethod integer, RequestFingerprint text);
            INSERT INTO Sales VALUES (1,0,120.50,0,'visitor-hash'), (0,0,85.25,2,'local-hash');
            """;
        command.ExecuteNonQuery(); command.CommandText = sql; command.ExecuteNonQuery();
        command.CommandText = "SELECT SaleCondition, Total, PaymentMethod, RequestFingerprint FROM Sales ORDER BY CustomerType DESC";
        using var reader = command.ExecuteReader();
        Assert.True(reader.Read()); Assert.Equal(0, reader.GetInt32(0)); Assert.Equal(120.50m, reader.GetDecimal(1)); Assert.Equal(0, reader.GetInt32(2)); Assert.Equal("visitor-hash", reader.GetString(3));
        Assert.True(reader.Read()); Assert.Equal(1, reader.GetInt32(0)); Assert.Equal(85.25m, reader.GetDecimal(1)); Assert.Equal(2, reader.GetInt32(2)); Assert.Equal("local-hash", reader.GetString(3));
    }
}
