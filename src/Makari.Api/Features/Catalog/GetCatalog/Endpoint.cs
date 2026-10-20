using Makari.Api.Features.Catalog.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using System.ComponentModel;
using Microsoft.AspNetCore.Mvc;

namespace Makari.Api.Features.Catalog.GetCatalog;

public static class GetCatalogEndpoint
{
    public static IEndpointRouteBuilder MapGetCatalogEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/catalog", HandleAsync)
            .WithName("GetCatalog")
            .WithTags("Catalog")
            .WithSummary("Get active product catalog")
            .WithDescription(
                "Returns active products with active prices. " +
                "Required query parameter: customerType (Local or Visitor).");
        return endpoints;
    }

    private static async Task<Results<Ok<GetCatalogResponse>, ValidationProblem>> HandleAsync(
        [FromQuery]
        [Description("Customer type: Local or Visitor.")]
        string? customerType,
        GetCatalogHandler handler,
        CancellationToken cancellationToken
    ){
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
