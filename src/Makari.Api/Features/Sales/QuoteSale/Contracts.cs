using System.Text.Json.Serialization;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.QuoteSale;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteSaleRequest(CustomerType? CustomerType,
    [property: JsonRequired] IReadOnlyList<QuoteSaleItemRequest> Items, SaleCondition? SaleCondition = null)
{
    [JsonIgnore] public SaleCondition EffectiveCondition => SaleCondition ?? (CustomerType == Catalog.Domain.CustomerType.Local ? Domain.SaleCondition.LocalDiscount : Domain.SaleCondition.Normal);
    [JsonIgnore] public CustomerType PricingType => EffectiveCondition == Domain.SaleCondition.LocalDiscount ? Catalog.Domain.CustomerType.Local : Catalog.Domain.CustomerType.Visitor;
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QuoteSaleItemRequest(Guid ProductVariantId, int Quantity);

public sealed record QuoteSaleResponse(
    CustomerType CustomerType, decimal Subtotal, decimal Discount, decimal Total,
    IReadOnlyList<QuoteSaleItemResponse> Items, SaleCondition SaleCondition = SaleCondition.Normal, long UnitsConsumed = 0);

public sealed record QuoteSaleItemResponse(
    Guid ProductVariantId, int Quantity, decimal VisitorUnitPrice, decimal UnitPrice,
    decimal Subtotal, decimal Discount, decimal Total);
