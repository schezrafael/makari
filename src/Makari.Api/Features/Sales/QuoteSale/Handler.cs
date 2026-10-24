using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;

namespace Makari.Api.Features.Sales.QuoteSale;

public sealed class QuoteSaleHandler(MakariDbContext context)
{
    public async Task<QuoteSaleOutcome> HandleAsync(QuoteSaleRequest request, CancellationToken cancellationToken = default)
    {
        var prices = await SalePricing.LoadAsync(context, request.Items.Select(item => item.ProductVariantId).ToArray(),
            request.CustomerType, cancellationToken);
        var byId = prices.ToDictionary(price => price.Id);
        if (request.Items.Any(item => !byId.TryGetValue(item.ProductVariantId, out var price) ||
            price.Price is null || price.VisitorPrice is null))
            return new(null, new() { ["items"] = ["Variants must be active and have both Visitor and requested customer prices."] });

        // A Local price above the base price is not a discount. Do not silently cap it:
        // CreateSale must still calculate exactly the configured Local price.
        if (prices.Any(price => price.Price > price.VisitorPrice))
            return new(null, new() { ["items"] = ["A Local price exceeds its Visitor price. The local discount cannot be quoted."] });

        var items = request.Items.Select(item =>
        {
            var price = byId[item.ProductVariantId];
            var subtotal = SaleAmounts.Subtotal(price.VisitorPrice!.Value, item.Quantity);
            var total = SaleAmounts.Subtotal(price.Price!.Value, item.Quantity);
            return new QuoteSaleItemResponse(item.ProductVariantId, item.Quantity,
                price.VisitorPrice.Value, price.Price.Value, subtotal, subtotal - total, total);
        }).ToArray();
        return new(new(request.CustomerType, items.Sum(item => item.Subtotal),
            items.Sum(item => item.Discount), items.Sum(item => item.Total), items), null);
    }
}

public sealed record QuoteSaleOutcome(QuoteSaleResponse? Response, Dictionary<string, string[]>? Errors);
