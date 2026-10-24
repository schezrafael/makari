namespace Makari.Api.Features.Sales.Domain;

public sealed class SaleItem
{
    private SaleItem() { }

    internal SaleItem(Sale sale, Guid productVariantId, int quantity, decimal unitPrice)
    {
        ArgumentNullException.ThrowIfNull(sale);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (productVariantId == Guid.Empty) throw new ArgumentException("A product variant is required.", nameof(productVariantId));
        if (unitPrice <= 0) throw new ArgumentOutOfRangeException(nameof(unitPrice));

        Id = Guid.NewGuid();
        Sale = sale;
        SaleId = sale.Id;
        ProductVariantId = productVariantId;
        Quantity = quantity;
        UnitPrice = unitPrice;
        Subtotal = SaleAmounts.Subtotal(unitPrice, quantity);
    }

    public Guid Id { get; private set; }
    public Guid SaleId { get; private set; }
    public Sale Sale { get; private set; } = null!;
    public Guid ProductVariantId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }
    public decimal Subtotal { get; private set; }
}
