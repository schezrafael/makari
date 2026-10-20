namespace Makari.Api.Features.Catalog.Domain;

public sealed class ProductVariant
{
    private readonly List<ProductPrice> _prices = [];

    private ProductVariant() { }

    internal ProductVariant(Product product, string name, int sizeMl)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sizeMl);
        Id = Guid.NewGuid();
        Product = product;
        ProductId = product.Id;
        Name = name.Trim();
        SizeMl = sizeMl;
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int SizeMl { get; private set; }
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<ProductPrice> Prices => _prices.AsReadOnly();

    public ProductPrice AddPrice(CustomerType customerType, decimal amount)
    {
        if (_prices.Any(price => price.CustomerType == customerType))
        {
            throw new InvalidOperationException("A price already exists for this customer type.");
        }

        var price = new ProductPrice(this, customerType, amount);
        _prices.Add(price);
        return price;
    }

    public void Deactivate() => IsActive = false;
}
