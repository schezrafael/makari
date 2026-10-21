using Makari.Api.Features.Catalog.Domain;

namespace Makari.Api.Features.Sales.Domain;

public sealed class Sale
{
    private readonly List<SaleItem> _items = [];

    private Sale() { }

    public Sale(
        CustomerType customerType,
        PaymentMethod paymentMethod,
        string registeredBy,
        Guid idempotencyKey,
        string requestFingerprint,
        DateTimeOffset createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        if (idempotencyKey == Guid.Empty) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        if (!Enum.IsDefined(customerType)) throw new ArgumentOutOfRangeException(nameof(customerType));
        if (!Enum.IsDefined(paymentMethod)) throw new ArgumentOutOfRangeException(nameof(paymentMethod));

        Id = Guid.NewGuid();
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        CustomerType = customerType;
        PaymentMethod = paymentMethod;
        RegisteredBy = registeredBy.Trim();
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
    }

    public Guid Id { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public CustomerType CustomerType { get; private set; }
    public PaymentMethod PaymentMethod { get; private set; }
    public decimal Total { get; private set; }
    public string RegisteredBy { get; private set; } = null!;
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = null!;
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    public SaleItem AddItem(Guid productVariantId, int quantity, decimal unitPrice)
    {
        if (_items.Any(item => item.ProductVariantId == productVariantId))
            throw new InvalidOperationException("A sale cannot contain the same variant more than once.");

        var item = new SaleItem(this, productVariantId, quantity, unitPrice);
        var newTotal = checked(Total + item.Subtotal);
        _items.Add(item);
        Total = newTotal;
        return item;
    }
}
