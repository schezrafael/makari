using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectedSaleAttempts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RejectedSaleAttempts",
                columns: table => new
                {
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpectedTotal = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    CurrentTotal = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: true),
                    ValidationErrorsJson = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RejectedSaleAttempts", x => x.IdempotencyKey);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RejectedSaleAttempts");
        }
    }
}
