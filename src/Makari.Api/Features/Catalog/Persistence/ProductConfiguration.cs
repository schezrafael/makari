using Makari.Api.Features.Catalog.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Catalog.Persistence;

public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Id).ValueGeneratedNever();
        builder.Property(product => product.Name).IsRequired();
        builder.Property(product => product.Category).IsRequired();
        builder.Property(product => product.IsActive).IsRequired();

        builder.HasMany(product => product.Variants)
            .WithOne(variant => variant.Product)
            .HasForeignKey(variant => variant.ProductId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(product => product.Variants)
            .HasField("_variants").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
