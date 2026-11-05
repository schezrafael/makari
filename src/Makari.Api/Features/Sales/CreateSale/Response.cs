using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public sealed record CreateSaleResponse(
    Guid Id,
    DateTimeOffset CreatedAtUtc,
    CustomerType CustomerType,
    PaymentMethod PaymentMethod,
    decimal Total,
    string RegisteredBy,
    Guid IdempotencyKey,
    IReadOnlyList<CreateSaleItemResponse> Items,
    Guid? CustomerId = null,
    string? CustomerUsernameSnapshot = null);

public sealed record CreateSaleItemResponse(
    Guid Id, Guid ProductVariantId, int Quantity, decimal UnitPrice, decimal Subtotal);
