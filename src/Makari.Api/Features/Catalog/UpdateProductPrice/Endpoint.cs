using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.UpdateProductPrice;

public sealed record Request(decimal? VisitorPrice, decimal? LocalPrice, Guid? Version);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapUpdateProductPriceEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/products/{id:guid}/variants/{variantId:guid}/prices", Handle).WithName("UpdateProductPrice").WithTags("Catalog administration").AddEndpointFilter<CatalogWriteFilter>();
        return endpoints;
    }

    private static async Task<IResult> Handle(Guid id, Guid variantId, Request request, MakariDbContext db, CancellationToken ct)
    {
        if (!CatalogAdministration.ValidPrice(request.VisitorPrice) || !CatalogAdministration.ValidPrice(request.LocalPrice))
            return CatalogAdministration.Invalid("prices", "Los precios son obligatorios, entre 0 y 9999999999.99, con máximo dos decimales.");
        if (request.LocalPrice > request.VisitorPrice)
            return CatalogAdministration.Invalid("localPrice", "El precio Local no puede superar Visitor para conservar la cotización del descuento local.");
        var product = await CatalogAdministration.Load(db, id, ct);
        if (product is null) return Results.NotFound();
        if (request.Version != product.Version) return CatalogAdministration.Stale();
        var variant = product.Variants.SingleOrDefault(v => v.Id == variantId);
        if (variant is null) return Results.NotFound();
        variant.SetPrice(CustomerType.Visitor, request.VisitorPrice!.Value);
        variant.SetPrice(CustomerType.Local, request.LocalPrice!.Value);
        return await CatalogAdministration.Save(db, product, ct);
    }
}
