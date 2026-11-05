using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCardPaymentMethod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_PaymentMethod_Valid",
                table: "Sales");

            migrationBuilder.AddColumn<decimal>(
                name: "CardSales",
                table: "DailyCashCloses",
                type: "numeric(28,2)",
                precision: 28,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_PaymentMethod_Valid",
                table: "Sales",
                sql: "\"PaymentMethod\" IN (0, 1, 2)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Sales_PaymentMethod_Valid",
                table: "Sales");

            migrationBuilder.DropColumn(
                name: "CardSales",
                table: "DailyCashCloses");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Sales_PaymentMethod_Valid",
                table: "Sales",
                sql: "\"PaymentMethod\" IN (0, 1)");
        }
    }
}
