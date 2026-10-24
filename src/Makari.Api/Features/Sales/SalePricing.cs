using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Sales;

// Both use cases read the same active variants and current prices. No entities are tracked.
internal static class SalePricing
{
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
