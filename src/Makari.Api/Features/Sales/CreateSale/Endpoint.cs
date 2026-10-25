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
            .WithDescription("Creates a sale only when expectedTotal matches current catalog prices. Replays return the original sale. HTTP 409 distinguishes price_changed from idempotency_conflict.");
        return endpoints;
    }

    private static async Task<Results<Created<CreateSaleResponse>, Ok<CreateSaleResponse>,
        ValidationProblem, Conflict<ProblemDetails>>> HandleAsync(
        CreateSaleRequest? request,
        CreateSaleHandler handler,
        CancellationToken cancellationToken)
    {
        var errors = CreateSaleValidator.Validate(request);
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors, extensions: new Dictionary<string, object?>
        {
            ["code"] = "request_validation_failed", ["idempotencyKey"] = request?.IdempotencyKey
        });

        var result = await handler.HandleAsync(request!, cancellationToken);
        if (result.Errors is not null) return TypedResults.ValidationProblem(result.Errors, extensions: new Dictionary<string, object?>
        {
            ["code"] = "sale_validation_failed", ["idempotencyKey"] = request!.IdempotencyKey
        });
        if (result.CurrentTotal is not null)
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Sale total changed",
                Detail = "No sale was created. Confirm the current total before submitting a new operation.",
                Extensions = { ["code"] = "price_changed", ["currentTotal"] = result.CurrentTotal,
                    ["idempotencyKey"] = request!.IdempotencyKey }
            });
        if (result.Conflict is not null)
        {
            return TypedResults.Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Idempotency key conflict",
                Detail = result.Conflict,
                Extensions = { ["code"] = "idempotency_conflict", ["idempotencyKey"] = request!.IdempotencyKey }
            });
        }
        return result.IsReplay
            ? TypedResults.Ok(result.Response!)
            : TypedResults.Created((string?)null, result.Response!);
    }
}
