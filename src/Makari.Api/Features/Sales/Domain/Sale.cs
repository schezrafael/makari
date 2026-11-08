using Makari.Api.Features.Catalog.Domain;

namespace Makari.Api.Features.Sales.Domain;

public sealed class Sale
{
    private readonly List<SaleItem> _items = [];

    private Sale() { }

    public Sale(
        CustomerType customerType,
        PaymentMethod? paymentMethod,
        string registeredBy,
        Guid idempotencyKey,
        string requestFingerprint,
        DateTimeOffset createdAtUtc,
        Guid? customerId = null,
        string? customerUsernameSnapshot = null,
        SaleCondition? saleCondition = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registeredBy);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        if (idempotencyKey == Guid.Empty) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        if (!Enum.IsDefined(customerType)) throw new ArgumentOutOfRangeException(nameof(customerType));
        if (paymentMethod is not null && !Enum.IsDefined(paymentMethod.Value)) throw new ArgumentOutOfRangeException(nameof(paymentMethod));

        SaleCondition = saleCondition ?? (customerType == CustomerType.Local ? SaleCondition.LocalDiscount : SaleCondition.Normal);
        if (!Enum.IsDefined(SaleCondition)) throw new ArgumentOutOfRangeException(nameof(saleCondition));
        if (SaleCondition.IsFree() && paymentMethod is not null) throw new ArgumentException("Free operations have no payment method.");
        CustomerId = customerId;
        CustomerUsernameSnapshot = customerUsernameSnapshot;
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
    public Guid? CustomerId { get; private set; }
    public string? CustomerUsernameSnapshot { get; private set; }
    public CustomerType CustomerType { get; private set; }
    public SaleCondition SaleCondition { get; private set; }
    public PaymentMethod? PaymentMethod { get; private set; }
    public decimal Total { get; private set; }
    public string RegisteredBy { get; private set; } = null!;
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = null!;
    public IReadOnlyCollection<SaleItem> Items => _items.AsReadOnly();

    internal void IdentifyBuyer(Guid customerId, string username)
    {
        CustomerId = customerId;
        CustomerUsernameSnapshot = username;
    }

    public SaleItem AddItem(Guid productVariantId, int quantity, decimal unitPrice, decimal? referenceUnitPrice = null)
    {
        if (SaleCondition.IsFree() && unitPrice != 0) throw new ArgumentException("Free operations must have zero charged prices.");
        if (_items.Any(item => item.ProductVariantId == productVariantId))
            throw new InvalidOperationException("A sale cannot contain the same variant more than once.");

        var item = new SaleItem(this, productVariantId, quantity, unitPrice, referenceUnitPrice);
        var newTotal = checked(Total + item.Subtotal);
        _items.Add(item);
        Total = newTotal;
        return item;
    }
}
