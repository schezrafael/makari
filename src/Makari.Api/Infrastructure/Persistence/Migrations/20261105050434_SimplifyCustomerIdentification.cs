using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SimplifyCustomerIdentification : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A prior version allowed named buyers called visitante/local. Reuse those rows
            // instead of merging IDs or rewriting historical sales and snapshots.
            migrationBuilder.Sql("""
                UPDATE "Customers" SET "Username" = 'visitante'
                WHERE "Id" = 'a1200000-0000-4000-8000-000000000001'
                  AND NOT EXISTS (SELECT 1 FROM "Customers" WHERE "NormalizedUsername" = 'visitante'
                                  AND "Id" <> 'a1200000-0000-4000-8000-000000000001');
                UPDATE "Customers" SET "Username" = 'local'
                WHERE "Id" = 'a1200000-0000-4000-8000-000000000002'
                  AND NOT EXISTS (SELECT 1 FROM "Customers" WHERE "NormalizedUsername" = 'local'
                                  AND "Id" <> 'a1200000-0000-4000-8000-000000000002');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: new Guid("a1200000-0000-4000-8000-000000000001"),
                column: "Username",
                value: "v");

            migrationBuilder.UpdateData(
                table: "Customers",
                keyColumn: "Id",
                keyValue: new Guid("a1200000-0000-4000-8000-000000000002"),
                column: "Username",
                value: "L");
        }
    }
}
