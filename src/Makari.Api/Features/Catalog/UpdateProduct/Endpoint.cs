using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.UpdateProduct;

public sealed record Request(string? Name, bool IsActive, Guid? Version);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapUpdateProductEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/api/products/{id:guid}", Handle).WithName("UpdateProduct").WithTags("Catalog administration").AddEndpointFilter<CatalogWriteFilter>();
        return endpoints;
    }

    private static async Task<IResult> Handle(Guid id, Request request, MakariDbContext db, CancellationToken ct)
    {
        if (!CatalogAdministration.ValidName(request.Name)) return CatalogAdministration.Invalid("name", "Escribe un nombre de 1 a 100 caracteres.");
        var product = await CatalogAdministration.Load(db, id, ct);
        if (product is null) return Results.NotFound();
        if (request.Version != product.Version) return CatalogAdministration.Stale();
        var name = request.Name!.Trim();
        if (await db.Products.AnyAsync(p => p.Id != id && p.Name.ToLower() == name.ToLower(), ct))
            return CatalogAdministration.Conflict("duplicate_product", "Ya existe una bebida con ese nombre.");
        product.Update(name, request.IsActive);
        return await CatalogAdministration.Save(db, product, ct);
    }
}
