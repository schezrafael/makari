using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ManageCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_Total_Positive",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SaleItems_Subtotal_Positive",
                table: "SaleItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SaleItems_UnitPrice_Positive",
                table: "SaleItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPrices_Amount_Positive",
                table: "ProductPrices");

            migrationBuilder.AddColumn<Guid>(
                name: "Version",
                table: "Products",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_Total_Positive",
                table: "Sales",
                sql: "\"Total\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SaleItems_Subtotal_Positive",
                table: "SaleItems",
                sql: "\"Subtotal\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SaleItems_UnitPrice_Positive",
                table: "SaleItems",
                sql: "\"UnitPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPrices_Amount_Positive",
                table: "ProductPrices",
                sql: "\"Amount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_Total_Positive",
                table: "Sales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SaleItems_Subtotal_Positive",
                table: "SaleItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SaleItems_UnitPrice_Positive",
                table: "SaleItems");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ProductPrices_Amount_Positive",
                table: "ProductPrices");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "Products");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_Total_Positive",
                table: "Sales",
                sql: "\"Total\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SaleItems_Subtotal_Positive",
                table: "SaleItems",
                sql: "\"Subtotal\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_SaleItems_UnitPrice_Positive",
                table: "SaleItems",
                sql: "\"UnitPrice\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ProductPrices_Amount_Positive",
                table: "ProductPrices",
                sql: "\"Amount\" > 0");
        }
    }
}
