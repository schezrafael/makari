using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.ListProducts;



public static class Endpoint
{
    public static IEndpointRouteBuilder MapListProductsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/products", Handle).WithName("ListProducts").WithTags("Catalog administration");
        return endpoints;
    }

    private static async Task<IResult> Handle(MakariDbContext db, CancellationToken ct)
    {
        var products = await db.Products.AsNoTracking().Include(p => p.Variants).ThenInclude(v => v.Prices)
            .OrderBy(p => p.Name).ToListAsync(ct);
        return Results.Ok(products.Select(ProductResponse.From).ToArray());
    }
}
