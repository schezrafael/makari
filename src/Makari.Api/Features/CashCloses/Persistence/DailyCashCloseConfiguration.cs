using Makari.Api.Features.CashCloses.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.CashCloses.Persistence;

public sealed class DailyCashCloseConfiguration : IEntityTypeConfiguration<DailyCashClose>
{
    public void Configure(EntityTypeBuilder<DailyCashClose> builder)
    {
        builder.ToTable("DailyCashCloses", table =>
        {
            table.HasCheckConstraint("CK_DailyCashCloses_SalesCount_NonNegative", "\"SalesCount\" >= 0");
            table.HasCheckConstraint("CK_DailyCashCloses_OpeningCash_NonNegative", "\"OpeningCash\" >= 0");
            table.HasCheckConstraint("CK_DailyCashCloses_CountedCash_NonNegative", "\"CountedCash\" >= 0");
        });
        builder.HasKey(close => close.Id);
        builder.Property(close => close.Id).ValueGeneratedNever();
        builder.Property(close => close.BusinessDate).HasColumnType("date").IsRequired();
        builder.Property(close => close.ClosedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(close => close.IdempotencyKey).IsRequired();
        builder.Property(close => close.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(close => close.OpeningCash).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.CashSales).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.TransferSales).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.CardSales).HasPrecision(28, 2).HasDefaultValue(0m).IsRequired();
        builder.Property(close => close.TotalRevenue).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.SalesCount).IsRequired();
        builder.Property(close => close.ExpectedCash).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.CountedCash).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.Difference).HasPrecision(28, 2).IsRequired();
        builder.Property(close => close.Responsible).HasMaxLength(100).IsRequired();
        builder.Property(close => close.Observations).HasMaxLength(2000);
        builder.HasIndex(close => close.BusinessDate).IsUnique();
        builder.HasIndex(close => close.IdempotencyKey).IsUnique();
    }
}
