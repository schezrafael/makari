using Makari.Api.Features.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Catalog.Persistence;

public sealed class ProductPriceConfiguration : IEntityTypeConfiguration<ProductPrice>
{
    public void Configure(EntityTypeBuilder<ProductPrice> builder)
    {
        builder.ToTable("ProductPrices", table =>
        {
            table.HasCheckConstraint("CK_ProductPrices_Amount_Positive", "\"Amount\" > 0");
            table.HasCheckConstraint("CK_ProductPrices_CustomerType_Valid", "\"CustomerType\" IN (0, 1)");
        });
        builder.HasKey(price => price.Id);
        builder.Property(price => price.Id).ValueGeneratedNever();
        builder.Property(price => price.CustomerType).HasConversion<int>().IsRequired();
        builder.Property(price => price.Amount).HasPrecision(12, 2).IsRequired();
        builder.HasIndex(price => new { price.ProductVariantId, price.CustomerType }).IsUnique();
    }
}
