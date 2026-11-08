using System.Text.Json.Serialization;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public sealed record CreateSaleRequest(
    Guid IdempotencyKey,
    CustomerType? CustomerType,
    PaymentMethod? PaymentMethod,
    IReadOnlyList<CreateSaleItemRequest> Items,
    [property: JsonRequired] decimal? ExpectedTotal = null,
    Guid? CustomerId = null,
    string? CustomerUsername = null,
    SaleCondition? SaleCondition = null)
{
    [JsonIgnore] public SaleCondition EffectiveCondition => SaleCondition ?? (CustomerType == Catalog.Domain.CustomerType.Local ? Domain.SaleCondition.LocalDiscount : Domain.SaleCondition.Normal);
    [JsonIgnore] public CustomerType PricingType => EffectiveCondition == Domain.SaleCondition.LocalDiscount ? Catalog.Domain.CustomerType.Local : Catalog.Domain.CustomerType.Visitor;
    [JsonIgnore] public PaymentMethod? EffectivePayment => Legacy ? PaymentMethod ?? Domain.PaymentMethod.Cash : PaymentMethod;
    [JsonIgnore] public bool Legacy => SaleCondition is null && CustomerType is not null;
}

public sealed record CreateSaleItemRequest(Guid ProductVariantId, int Quantity);
