using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Sales;

// Both use cases read the same active variants and current prices. No entities are tracked.
internal static class SalePricing
{
    public static async Task<List<VariantPrice>> LoadConditionAsync(MakariDbContext context, Guid[] ids, SaleCondition condition, CancellationToken ct)
    {
        var prices = await LoadAsync(context, ids, condition == SaleCondition.LocalDiscount ? CustomerType.Local : CustomerType.Visitor, ct);
        return prices.Select(price => price with { Price = price.VisitorPrice is null ? null : condition.IsFree() ? 0m : price.Price > price.VisitorPrice ? null : price.Price }).ToList();
    }

    public static Task<List<VariantPrice>> LoadAsync(
        MakariDbContext context, Guid[] variantIds, CustomerType customerType, CancellationToken cancellationToken) =>
        context.ProductVariants.AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id) && variant.IsActive && variant.Product.IsActive)
            .Select(variant => new VariantPrice(
                variant.Id,
                variant.Prices.Where(price => price.CustomerType == customerType)
                    .Select(price => (decimal?)price.Amount).SingleOrDefault(),
                variant.Prices.Where(price => price.CustomerType == CustomerType.Visitor)
                    .Select(price => (decimal?)price.Amount).SingleOrDefault()))
            .ToListAsync(cancellationToken);
}

internal sealed record VariantPrice(Guid Id, decimal? Price, decimal? VisitorPrice);
