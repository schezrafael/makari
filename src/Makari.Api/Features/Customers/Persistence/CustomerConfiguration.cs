using Makari.Api.Features.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Customers.Persistence;

public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers", table => table.HasCheckConstraint("CK_Customers_CustomerType_Valid", "\"CustomerType\" IN (0, 1)"));
        builder.Property(customer => customer.CustomerType).HasConversion<int>().HasDefaultValue(Makari.Api.Features.Catalog.Domain.CustomerType.Visitor).ValueGeneratedNever();
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.Id).ValueGeneratedNever();
        builder.Property(customer => customer.Username).HasMaxLength(40).IsRequired();
        builder.Property(customer => customer.NormalizedUsername).HasMaxLength(40)
            .HasComputedColumnSql("lower(\"Username\")", stored: true).IsRequired();
        builder.HasIndex(customer => customer.NormalizedUsername).IsUnique();
        builder.HasData(new { Id = Customer.VisitorId, Username = "visitante", CustomerType = Makari.Api.Features.Catalog.Domain.CustomerType.Visitor },
            new { Id = Customer.LocalId, Username = "local", CustomerType = Makari.Api.Features.Catalog.Domain.CustomerType.Visitor });
    }
}
