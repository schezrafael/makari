using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CustomerClassificationAndSaleConditions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "PaymentMethod",
                table: "Sales",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddColumn<int>(
                name: "SaleCondition",
                table: "Sales",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ReferenceUnitPrice",
                table: "SaleItems",
                type: "numeric(12,2)",
                precision: 12,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustomerType",
                table: "Customers",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            // Preserve amounts, payment methods, fingerprints, rejected attempts and confirmed closes.
            // CustomerType on Sales remains the legacy price selector, never buyer classification.
            migrationBuilder.Sql("""
                UPDATE "Sales" SET "SaleCondition" = CASE "CustomerType" WHEN 0 THEN 1 ELSE 0 END;
                """);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_FreeCondition",
                table: "Sales",
                sql: "\"SaleCondition\" IN (0, 1) OR (CAST(\"Total\" AS numeric) = 0 AND \"PaymentMethod\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_SaleCondition_Valid",
                table: "Sales",
                sql: "\"SaleCondition\" IN (0, 1, 2, 3, 4)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Customers_CustomerType_Valid",
                table: "Customers",
                sql: "\"CustomerType\" IN (0, 1)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Never turn a no-charge operation into a cash payment during rollback.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Sales" WHERE "SaleCondition" IN (2,3,4) OR "PaymentMethod" IS NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade: no-charge sales exist. Preserve history and roll forward.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_FreeCondition",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_SaleCondition_Valid",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Customers_CustomerType_Valid",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "SaleCondition",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "ReferenceUnitPrice",
                table: "SaleItems");

            migrationBuilder.DropColumn(
                name: "CustomerType",
                table: "Customers");

            migrationBuilder.AlterColumn<int>(
                name: "PaymentMethod",
                table: "Sales",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
