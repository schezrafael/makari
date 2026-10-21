using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Makari.Api.Features.Sales.CreateSale;

public static class CreateSaleEndpoint
{
    public static IEndpointRouteBuilder MapCreateSaleEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sales", HandleAsync)
            .WithName("CreateSale")
            .WithTags("Sales")
            .WithSummary("Create a sale")
            .WithDescription("Creates a sale using active catalog prices and an idempotency key.");
        return endpoints;
    }

    private static async Task<Results<Created<CreateSaleResponse>, Ok<CreateSaleResponse>,
        ValidationProblem, Conflict<ProblemDetails>>> HandleAsync(
        CreateSaleRequest? request,
        CreateSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var errors = CreateSaleValidator.Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);

        var result = await handler.HandleAsync(request!, cancellationToken);
        if (result.Errors is not null) return TypedResults.ValidationProblem(result.Errors);
        if (result.Conflict is not null)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Idempotency key conflict",
                Detail = result.Conflict
            });
        }
        return result.IsReplay
            ? TypedResults.Ok(result.Response!)
            : TypedResults.Created((string?)null, result.Response!);
    }
}
