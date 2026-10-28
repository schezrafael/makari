using System.Globalization;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace Makari.Api.Features.CashCloses.GetDailyCashClose;

public static class GetDailyCashCloseEndpoint
{
    public static IEndpointRouteBuilder MapGetDailyCashCloseEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/cash-closes", HandleAsync)
            .WithName("GetDailyCashClose")
            .WithTags("Cash closes")
            .WithSummary("Get a confirmed close by business date")
            .WithDescription("Returns 404 when the requested business date has not been closed. Omitted date means the current business-local date.");
        return endpoints;
    }

    private static async Task<Results<Ok<DailyCashCloseResponse>, NotFound<ProblemDetails>, ValidationProblem>> HandleAsync(
        string? date, HttpRequest request, GetDailyCashCloseHandler handler, CancellationToken cancellationToken)
    {
        DateOnly businessDate;
        if (!request.Query.ContainsKey("date")) businessDate = handler.Today;
        else if (date is null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out businessDate) || businessDate == DateOnly.MaxValue)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["date"] = ["Specify date as a valid calendar date in YYYY-MM-DD format."]
            });

        var result = await handler.ByDateAsync(businessDate, cancellationToken);
        return result is null
            ? TypedResults.NotFound(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound,
                Title = "Cash close not found",
                Detail = $"No confirmed cash close exists for {businessDate:yyyy-MM-dd}.",
                Extensions = { ["code"] = "cash_close_not_found", ["businessDate"] = businessDate }
            })
            : TypedResults.Ok(result);
    }
}
