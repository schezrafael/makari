namespace Makari.Api.Features.Catalog.Domain;

public sealed class Product
{
    private readonly List<ProductVariant> _variants = [];

    private Product() { }

    public Product(string name, string category)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(category);
        Id = Guid.NewGuid();
        Name = name.Trim();
        Category = category.Trim();
        IsActive = true;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public IReadOnlyCollection<ProductVariant> Variants => _variants.AsReadOnly();

    public ProductVariant AddVariant(string name, int sizeMl)
    {
        var variant = new ProductVariant(this, name, sizeMl);
        _variants.Add(variant);
        return variant;
    }

    public Guid Version { get; private set; } = Guid.NewGuid();

    public void Revise() => Version = Guid.NewGuid();

    public void Update(string name, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name.Trim();
        IsActive = isActive;
    }

    public void Deactivate() => IsActive = false;
}
