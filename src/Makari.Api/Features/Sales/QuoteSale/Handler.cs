using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;

namespace Makari.Api.Features.Sales.QuoteSale;

public sealed class QuoteSaleHandler(MakariDbContext context)
{
    public async Task<QuoteSaleOutcome> HandleAsync(QuoteSaleRequest request, CancellationToken cancellationToken = default)
    {
        var prices = await SalePricing.LoadConditionAsync(context, request.Items.Select(item => item.ProductVariantId).ToArray(),
            request.EffectiveCondition, cancellationToken);
        var byId = prices.ToDictionary(price => price.Id);
        if (request.Items.Any(item => !byId.TryGetValue(item.ProductVariantId, out var price) ||
            price.Price is null || price.VisitorPrice is null))
            return new(null, new() { ["items"] = ["Variants must be active and have both Visitor and requested customer prices."] });

        var items = request.Items.Select(item =>
        {
            var price = byId[item.ProductVariantId];
            var subtotal = SaleAmounts.Subtotal(price.VisitorPrice!.Value, item.Quantity);
            var total = SaleAmounts.Subtotal(price.Price!.Value, item.Quantity);
            return new QuoteSaleItemResponse(item.ProductVariantId, item.Quantity,
                price.VisitorPrice.Value, price.Price.Value, subtotal, subtotal - total, total);
        }).ToArray();
        return new(new(request.PricingType, items.Sum(item => item.Subtotal),
            items.Sum(item => item.Discount), items.Sum(item => item.Total), items, request.EffectiveCondition, items.Sum(item => (long)item.Quantity)), null);
    }
}

public sealed record QuoteSaleOutcome(QuoteSaleResponse? Response, Dictionary<string, string[]>? Errors);
