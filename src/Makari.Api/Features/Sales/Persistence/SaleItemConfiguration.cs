using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Sales.Persistence;

public sealed class SaleItemConfiguration : IEntityTypeConfiguration<SaleItem>
{
    public void Configure(EntityTypeBuilder<SaleItem> builder)
    {
        builder.ToTable("SaleItems", table =>
        {
            table.HasCheckConstraint("CK_SaleItems_Quantity_Positive", "\"Quantity\" > 0");
            table.HasCheckConstraint("CK_SaleItems_UnitPrice_Positive", "\"UnitPrice\" >= 0");
            table.HasCheckConstraint("CK_SaleItems_Subtotal_Positive", "\"Subtotal\" >= 0");
        });
        builder.Property(item => item.ReferenceUnitPrice).HasPrecision(12, 2);
        builder.HasKey(item => item.Id);
        builder.Property(item => item.Id).ValueGeneratedNever();
        builder.Property(item => item.ProductVariantId).IsRequired();
        builder.Property(item => item.Quantity).IsRequired();
        builder.Property(item => item.UnitPrice).HasPrecision(12, 2).IsRequired();
        builder.Property(item => item.Subtotal).HasPrecision(28, 2).IsRequired();
        builder.HasIndex(item => new { item.SaleId, item.ProductVariantId }).IsUnique();
        builder.HasIndex(item => item.ProductVariantId);
        builder.HasOne<ProductVariant>()
            .WithMany()
            .HasForeignKey(item => item.ProductVariantId)
            .IsRequired().OnDelete(DeleteBehavior.Restrict);
    }
}
