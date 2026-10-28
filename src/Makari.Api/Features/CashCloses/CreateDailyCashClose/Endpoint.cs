using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Makari.Api.Features.CashCloses.CreateDailyCashClose;

public static class CreateDailyCashCloseEndpoint
{
    public static IEndpointRouteBuilder MapCreateDailyCashCloseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/cash-closes", HandleAsync)
            .WithName("CreateDailyCashClose")
            .WithTags("Cash closes")
            .WithSummary("Confirm an immutable daily cash close")
            .WithDescription("Sales totals are calculated from persisted sales. Repeating an identical request with the same idempotencyKey returns the original close.");
        return endpoints;
    }

    private static async Task<Results<Created<DailyCashCloseResponse>, Ok<DailyCashCloseResponse>,
        ValidationProblem, Conflict<ProblemDetails>>> HandleAsync(
        CreateDailyCashCloseRequest? request,
        CreateDailyCashCloseHandler handler,
        CancellationToken cancellationToken)
    {
        var errors = CreateDailyCashCloseValidator.Validate(request, handler.Today);
        if (errors.Count > 0)
            return TypedResults.ValidationProblem(errors, extensions: new Dictionary<string, object?>
            {
                ["code"] = "cash_close_validation_failed",
                ["idempotencyKey"] = request?.IdempotencyKey
            });

        var result = await handler.HandleAsync(request!, cancellationToken);
        if (result.Errors is not null)
            return TypedResults.ValidationProblem(result.Errors, extensions: new Dictionary<string, object?>
            {
                ["code"] = "cash_close_validation_failed",
                ["idempotencyKey"] = request!.IdempotencyKey
            });
        if (result.ConflictCode is not null)
        {
            var details = new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = result.ConflictCode == "cash_close_exists" ? "Date already closed" : "Idempotency key conflict",
                Detail = result.ConflictDetail
            };
            details.Extensions["code"] = result.ConflictCode;
            details.Extensions["idempotencyKey"] = request!.IdempotencyKey;
            if (result.ExistingCloseId is not null) details.Extensions["existingCloseId"] = result.ExistingCloseId;
            details.Extensions["businessDate"] = request.BusinessDate;
            return TypedResults.Conflict(details);
        }

        return result.IsReplay
            ? TypedResults.Ok(result.Response!)
            : TypedResults.Created($"/api/cash-closes/{result.Response!.Id}", result.Response);
    }
}
