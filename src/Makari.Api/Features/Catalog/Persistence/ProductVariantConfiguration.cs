using Makari.Api.Features.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Catalog.Persistence;

public sealed class ProductVariantConfiguration : IEntityTypeConfiguration<ProductVariant>
{
    public void Configure(EntityTypeBuilder<ProductVariant> builder)
    {
        builder.ToTable("ProductVariants", table =>
            table.HasCheckConstraint("CK_ProductVariants_SizeMl_Positive", "\"SizeMl\" > 0"));
        builder.HasKey(variant => variant.Id);
        builder.Property(variant => variant.Id).ValueGeneratedNever();
        builder.Property(variant => variant.Name).IsRequired();
        builder.Property(variant => variant.SizeMl).IsRequired();
        builder.Property(variant => variant.IsActive).IsRequired();
        builder.HasIndex(variant => variant.ProductId);

        builder.HasMany(variant => variant.Prices)
            .WithOne(price => price.ProductVariant)
            .HasForeignKey(price => price.ProductVariantId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(variant => variant.Prices)
            .HasField("_prices").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
