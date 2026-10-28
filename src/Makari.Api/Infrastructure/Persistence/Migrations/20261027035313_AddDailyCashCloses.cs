using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Makari.Api.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDailyCashCloses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "ConflictBusinessDate",
                table: "RejectedSaleAttempts",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConflictCode",
                table: "RejectedSaleAttempts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DailyCashCloses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ClosedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OpeningCash = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    CashSales = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    TransferSales = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    TotalRevenue = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    SalesCount = table.Column<int>(type: "integer", nullable: false),
                    ExpectedCash = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    CountedCash = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    Difference = table.Column<decimal>(type: "numeric(28,2)", precision: 28, scale: 2, nullable: false),
                    Responsible = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Observations = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DailyCashCloses", x => x.Id);
                    table.CheckConstraint("CK_DailyCashCloses_CountedCash_NonNegative", "\"CountedCash\" >= 0");
                    table.CheckConstraint("CK_DailyCashCloses_OpeningCash_NonNegative", "\"OpeningCash\" >= 0");
                    table.CheckConstraint("CK_DailyCashCloses_SalesCount_NonNegative", "\"SalesCount\" >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DailyCashCloses_BusinessDate",
                table: "DailyCashCloses",
                column: "BusinessDate",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DailyCashCloses_IdempotencyKey",
                table: "DailyCashCloses",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DailyCashCloses");

            migrationBuilder.DropColumn(
                name: "ConflictBusinessDate",
                table: "RejectedSaleAttempts");

            migrationBuilder.DropColumn(
                name: "ConflictCode",
                table: "RejectedSaleAttempts");
        }
    }
}
