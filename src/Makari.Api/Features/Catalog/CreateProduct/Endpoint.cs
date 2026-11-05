using Makari.Api.Features.Catalog.Administration;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.CreateProduct;

public sealed record Request(string? Name, string? Category);

public static class Endpoint
{
    public static IEndpointRouteBuilder MapCreateProductEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/products", Handle).WithName("CreateProduct").WithTags("Catalog administration").AddEndpointFilter<CatalogWriteFilter>();
        return endpoints;
    }

    private static async Task<IResult> Handle(Request request, MakariDbContext db, CancellationToken ct)
    {
        if (!CatalogAdministration.ValidName(request.Name)) return CatalogAdministration.Invalid("name", "Escribe un nombre de 1 a 100 caracteres.");
        if (!CatalogAdministration.ValidName(request.Category)) return CatalogAdministration.Invalid("category", "Escribe una categoría de 1 a 100 caracteres.");
        var name = request.Name!.Trim();
        if (await db.Products.AnyAsync(p => p.Name.ToLower() == name.ToLower(), ct))
            return CatalogAdministration.Conflict("duplicate_product", "Ya existe una bebida con ese nombre.");
        var product = new Product(name, request.Category!);
        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/products/{product.Id}", ProductResponse.From(product));
    }
}
