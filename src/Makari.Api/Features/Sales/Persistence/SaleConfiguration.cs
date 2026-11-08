using Makari.Api.Features.Sales.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Sales.Persistence;

public sealed class SaleConfiguration : IEntityTypeConfiguration<Sale>
{
    public void Configure(EntityTypeBuilder<Sale> builder)
    {
        builder.ToTable("Sales", table =>
        {
            table.HasCheckConstraint("CK_Sales_SaleCondition_Valid", "\"SaleCondition\" IN (0, 1, 2, 3, 4)");
            table.HasCheckConstraint("CK_Sales_FreeCondition", "\"SaleCondition\" IN (0, 1) OR (CAST(\"Total\" AS numeric) = 0 AND \"PaymentMethod\" IS NULL)");
            table.HasCheckConstraint("CK_Sales_Total_Positive", "\"Total\" >= 0");
            table.HasCheckConstraint("CK_Sales_CustomerType_Valid", "\"CustomerType\" IN (0, 1)");
            table.HasCheckConstraint("CK_Sales_PaymentMethod_Valid", "\"PaymentMethod\" IN (0, 1, 2)");
        });
        builder.Property(sale => sale.SaleCondition).HasConversion<int>().IsRequired();
        builder.HasKey(sale => sale.Id);
        builder.Property(sale => sale.Id).ValueGeneratedNever();
        builder.Property(sale => sale.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(sale => sale.CustomerUsernameSnapshot).HasMaxLength(40);
        builder.HasOne<Makari.Api.Features.Customers.Domain.Customer>().WithMany()
            .HasForeignKey(sale => sale.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(sale => sale.CustomerType).HasConversion<int>().IsRequired();
        builder.Property(sale => sale.PaymentMethod).HasConversion<int>();
        builder.Property(sale => sale.Total).HasPrecision(28, 2).IsRequired();
        builder.Property(sale => sale.RegisteredBy).HasMaxLength(100).IsRequired();
        builder.Property(sale => sale.IdempotencyKey).IsRequired();
        builder.Property(sale => sale.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.HasIndex(sale => sale.IdempotencyKey).IsUnique();
        builder.Navigation(sale => sale.Items).HasField("_items").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(sale => sale.Items).WithOne(item => item.Sale)
            .HasForeignKey(item => item.SaleId).IsRequired().OnDelete(DeleteBehavior.Cascade);
    }
}
