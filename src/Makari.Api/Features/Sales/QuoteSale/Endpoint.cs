using Microsoft.AspNetCore.Http.HttpResults;
using Makari.Api.Features.Sales.CreateSale;

namespace Makari.Api.Features.Sales.QuoteSale;

public static class QuoteSaleEndpoint
{
    public static IEndpointRouteBuilder MapQuoteSaleEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/sales/quote", HandleAsync)
            .WithName("QuoteSale")
            .WithTags("Sales")
            .WithSummary("Quote a sale using current catalog prices")
            .WithDescription("Returns reference subtotal, discount, charged total and consumed units by saleCondition. Legacy customerType remains accepted for compatibility. Never writes a sale; CreateSale revalidates prices.");
        return endpoints;
    }

    private static async Task<Results<Ok<QuoteSaleResponse>, ValidationProblem>> HandleAsync(
        QuoteSaleRequest? request, QuoteSaleHandler handler, CancellationToken cancellationToken)
    {
        if (request is null)
            return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["request"] = ["A request body is required."] });
        var errors = SaleOrderValidator.Validate(request.CustomerType ?? request.PricingType, request.Items?.Select(item =>
            item is null ? null! : new CreateSaleItemRequest(item.ProductVariantId, item.Quantity)).ToArray());
        if (!Enum.IsDefined(request.EffectiveCondition)) errors["saleCondition"] = ["Specify a valid sale condition."];
        if (errors.Count > 0) return TypedResults.ValidationProblem(errors);
        var result = await handler.HandleAsync(request, cancellationToken);
        return result.Errors is not null
            ? TypedResults.ValidationProblem(result.Errors)
            : TypedResults.Ok(result.Response!);
    }
}
