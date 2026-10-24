using System.Text.Json.Serialization;
using Makari.Api.Features.Catalog.Domain;

namespace Makari.Api.Features.Sales.QuoteSale;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteSaleRequest([property: JsonRequired] CustomerType CustomerType,
    [property: JsonRequired] IReadOnlyList<QuoteSaleItemRequest> Items);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteSaleItemRequest(Guid ProductVariantId, int Quantity);

public sealed record QuoteSaleResponse(
    CustomerType CustomerType, decimal Subtotal, decimal Discount, decimal Total,
    IReadOnlyList<QuoteSaleItemResponse> Items);

public sealed record QuoteSaleItemResponse(
    Guid ProductVariantId, int Quantity, decimal VisitorUnitPrice, decimal UnitPrice,
    decimal Subtotal, decimal Discount, decimal Total);
