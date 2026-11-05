using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.CreateProductVariant;

public sealed record Request(string? Name, int SizeMl, decimal? VisitorPrice, decimal? LocalPrice, Guid? Version);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCreateProductVariantEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/products/{id:guid}/variants", Handle).WithName("CreateProductVariant").WithTags("Catalog administration").AddEndpointFilter<CatalogWriteFilter>();
        return endpoints;
    }

    private static async Task<IResult> Handle(Guid id, Request request, MakariDbContext db, CancellationToken ct)
    {
        if (!CatalogAdministration.ValidName(request.Name) || request.SizeMl <= 0)
            return CatalogAdministration.Invalid("variant", "Indica nombre (máximo 100 caracteres) y presentación en ml mayor que cero.");
        if (!CatalogAdministration.ValidPrice(request.VisitorPrice) || !CatalogAdministration.ValidPrice(request.LocalPrice))
            return CatalogAdministration.Invalid("prices", "Los precios son obligatorios, entre 0 y 9999999999.99, con máximo dos decimales.");
        if (request.LocalPrice > request.VisitorPrice)
            return CatalogAdministration.Invalid("localPrice", "El precio Local no puede superar Visitor para conservar la cotización del descuento local.");
        var product = await CatalogAdministration.Load(db, id, ct);
        if (product is null) return Results.NotFound();
        if (request.Version != product.Version) return CatalogAdministration.Stale();
        if (CatalogAdministration.DuplicateVariant(product, request.Name!, request.SizeMl))
            return CatalogAdministration.Conflict("duplicate_variant", "Ya existe esa variante y presentación.");
        var variant = product.AddVariant(request.Name!, request.SizeMl);
        variant.AddPrice(CustomerType.Visitor, request.VisitorPrice!.Value);
        variant.AddPrice(CustomerType.Local, request.LocalPrice!.Value);
        product.Revise();
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/products/{id}/variants/{variant.Id}", ProductResponse.From(product));
    }
}
