using Microsoft.AspNetCore.Http.HttpResults;

namespace Makari.Api.Features.Catalog.GetCatalog;

public static class GetCatalogEndpoint
{
    public static IEndpointRouteBuilder MapGetCatalogEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalog", HandleAsync)
            .WithName("GetCatalog")
            .WithTags("Catalog")
            .WithSummary("Get active product catalog with visitor prices")
            .WithDescription(
                "Returns active products and active variants with Visitor prices. No query parameters are required. " +
                "The obsolete customerType query parameter is rejected with HTTP 400, regardless of its value.");
        return endpoints;
    }

    private static async Task<Results<Ok<GetCatalogResponse>, ValidationProblem>> HandleAsync(
        HttpRequest request,
        GetCatalogHandler handler,
        CancellationToken cancellationToken
    ){
        // Reject legacy clients rather than silently returning Visitor prices for Local requests.
        if (request.Query.ContainsKey("customerType"))
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["customerType"] = ["customerType is no longer supported. Omit it; the catalog always returns Visitor prices."]
            });
        }

        return TypedResults.Ok(await handler.HandleAsync(cancellationToken));
    }
}
