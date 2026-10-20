namespace Makari.Api.Features.Catalog.Domain;

public sealed class ProductPrice
{
    private ProductPrice() { }

    internal ProductPrice(ProductVariant variant, CustomerType customerType, decimal amount)
    {
        ArgumentNullException.ThrowIfNull(variant);
        if (!Enum.IsDefined(customerType))
        {
            throw new ArgumentOutOfRangeException(nameof(customerType));
        }

        if (amount <= 0 || amount > 9_999_999_999.99m || decimal.Round(amount, 2) != amount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount),
                "Amount must be positive and fit decimal(12,2) without rounding.");
        }

        Id = Guid.NewGuid();
        ProductVariant = variant;
        ProductVariantId = variant.Id;
        CustomerType = customerType;
        Amount = amount;
    }

    public Guid Id { get; private set; }
    public Guid ProductVariantId { get; private set; }
    public ProductVariant ProductVariant { get; private set; } = null!;
    public CustomerType CustomerType { get; private set; }
    public decimal Amount { get; private set; }
}
