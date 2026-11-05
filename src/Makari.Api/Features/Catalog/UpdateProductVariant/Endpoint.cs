using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.UpdateProductVariant;

public sealed record Request(string? Name, int SizeMl, bool IsActive, Guid? Version);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapUpdateProductVariantEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/products/{id:guid}/variants/{variantId:guid}", Handle).WithName("UpdateProductVariant").WithTags("Catalog administration").AddEndpointFilter<CatalogWriteFilter>();
        return endpoints;
    }

    private static async Task<IResult> Handle(Guid id, Guid variantId, Request request, MakariDbContext db, CancellationToken ct)
    {
        if (!CatalogAdministration.ValidName(request.Name) || request.SizeMl <= 0)
            return CatalogAdministration.Invalid("variant", "Indica nombre (máximo 100 caracteres) y presentación en ml mayor que cero.");
        var product = await CatalogAdministration.Load(db, id, ct);
        if (product is null) return Results.NotFound();
        if (request.Version != product.Version) return CatalogAdministration.Stale();
        var variant = product.Variants.SingleOrDefault(v => v.Id == variantId);
        if (variant is null) return Results.NotFound();
        if (CatalogAdministration.DuplicateVariant(product, request.Name!, request.SizeMl, variantId))
            return CatalogAdministration.Conflict("duplicate_variant", "Ya existe esa variante y presentación.");
        variant.Update(request.Name!, request.SizeMl, request.IsActive);
        return await CatalogAdministration.Save(db, product, ct);
    }
}
