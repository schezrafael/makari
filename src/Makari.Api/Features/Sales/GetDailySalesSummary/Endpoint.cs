using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Makari.Api.Features.Sales.GetDailySalesSummary;

public static class GetDailySalesSummaryEndpoint
{
    public static IEndpointRouteBuilder MapGetDailySalesSummaryEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/sales/summary", HandleAsync)
            .WithName("GetDailySalesSummary")
            .WithTags("Sales")
            .WithSummary("Get daily sales summary")
            .WithDescription("Summarizes completed sales for a business-local date. If date is omitted, uses today's date in Business:TimeZoneId.");
        return endpoints;
    }

    private static async Task<Results<Ok<GetDailySalesSummaryResponse>, ValidationProblem>> HandleAsync(
        string? date,
        HttpRequest request,
        GetDailySalesSummaryHandler handler,
        CancellationToken cancellationToken)
    {
        DateOnly requestedDate;
        if (!request.Query.ContainsKey("date"))
        {
            requestedDate = handler.Today;
        }
        else if (date is null || !DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                     DateTimeStyles.None, out requestedDate) || requestedDate == DateOnly.MaxValue)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["date"] = ["Specify date as a valid calendar date in YYYY-MM-DD format."]
            });
        }

        return TypedResults.Ok(await handler.HandleAsync(requestedDate, cancellationToken));
    }
}
