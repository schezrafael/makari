using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;

namespace Makari.Api.Features.CashCloses.GetDailyCashCloseById;

public static class GetDailyCashCloseByIdEndpoint
{
    public static IEndpointRouteBuilder MapGetDailyCashCloseByIdEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cash-closes/{id:guid}", HandleAsync)
            .WithName("GetDailyCashCloseById")
            .WithTags("Cash closes")
            .WithSummary("Get a confirmed cash close by id");
        return endpoints;
    }

    private static async Task<Results<Ok<DailyCashCloseResponse>, NotFound<ProblemDetails>>> HandleAsync(
        Guid id, GetDailyCashCloseByIdHandler handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(id, cancellationToken);
        return result is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Cash close not found",
                Extensions = { ["code"] = "cash_close_not_found" }
            })
            : TypedResults.Ok(result);
    }
}
