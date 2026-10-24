namespace Makari.Api.Features.Sales.CreateSale;

public static class CreateSaleValidator
{
    public static Dictionary<string, string[]> Validate(CreateSaleRequest? request)
    {
        if (request is null) return new() { ["request"] = ["A request body is required."] };
        var errors = SaleOrderValidator.Validate(request.CustomerType, request.Items);
        if (request.IdempotencyKey == Guid.Empty) errors["idempotencyKey"] = ["A non-empty idempotency key is required."];
        if (!Enum.IsDefined(request.PaymentMethod)) errors["paymentMethod"] = ["Specify Cash or Transfer."];
        return errors;
    }
}
