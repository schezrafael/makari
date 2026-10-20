using Makari.Api.Features.Catalog.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Makari.Api.Features.Catalog.GetCatalog;

public static class GetCatalogEndpoint
{
    public static IEndpointRouteBuilder MapGetCatalogEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalog", HandleAsync)
            .WithName("GetCatalog")
            .WithTags("Catalog");
        return endpoints;
    }

    private static async Task<Results<Ok<GetCatalogResponse>, ValidationProblem>> HandleAsync(
        string? customerType, GetCatalogHandler handler, CancellationToken cancellationToken)
    {
        // Do not use Enum.TryParse: numeric and combined values are not part of the HTTP contract.
        CustomerType? parsed = customerType switch
        {
            nameof(CustomerType.Local) => CustomerType.Local,
            nameof(CustomerType.Visitor) => CustomerType.Visitor,
            _ => null
        };
        if (parsed is null)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["customerType"] = ["Specify customerType as Local or Visitor."]
            });
        }

        return TypedResults.Ok(await handler.HandleAsync(parsed.Value, cancellationToken));
    }
}
