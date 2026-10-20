namespace Makari.Api.Features.Catalog.GetCatalog;

public sealed record GetCatalogResponse(IReadOnlyList<CatalogProductResponse> Products);

public sealed record CatalogProductResponse(
    Guid Id, string Name, string Category, IReadOnlyList<CatalogVariantResponse> Variants);

public sealed record CatalogVariantResponse(Guid Id, string Name, int SizeMl, decimal Price);
