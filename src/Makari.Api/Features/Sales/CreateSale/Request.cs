using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public sealed record CreateSaleRequest(
    Guid IdempotencyKey,
    CustomerType CustomerType,
    PaymentMethod PaymentMethod,
    IReadOnlyList<CreateSaleItemRequest> Items);

public sealed record CreateSaleItemRequest(Guid ProductVariantId, int Quantity);
