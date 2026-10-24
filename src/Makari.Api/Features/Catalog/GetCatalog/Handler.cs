using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.GetCatalog;

public sealed class GetCatalogHandler(MakariDbContext context)
{
    public async Task<GetCatalogResponse> HandleAsync(
        CancellationToken cancellationToken = default)
    {
        var products = await context.Products
            .AsNoTracking()
            .Where(product => product.IsActive)
            .OrderBy(product => product.Name).ThenBy(product => product.Id)
            .Select(product => new CatalogProductResponse(
                product.Id,
                product.Name,
                product.Category,
                product.Variants
                    .Where(variant => variant.IsActive &&
                        variant.Prices.Any(price => price.CustomerType == CustomerType.Visitor))
                    .OrderBy(variant => variant.Name).ThenBy(variant => variant.Id)
                    .Select(variant => new CatalogVariantResponse(
                        variant.Id,
                        variant.Name,
                        variant.SizeMl,
                        variant.Prices
                            .Where(price => price.CustomerType == CustomerType.Visitor)
                            .Select(price => price.Amount)
                            .First()))
                    .ToList()))
            .ToListAsync(cancellationToken);

        return new GetCatalogResponse(products);
    }
}
