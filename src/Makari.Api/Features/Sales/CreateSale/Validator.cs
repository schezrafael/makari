using Makari.Api.Features.Customers.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public static class CreateSaleValidator
{
    public static Dictionary<string, string[]> Validate(CreateSaleRequest? request)
    {
        if (request is null) return new() { ["request"] = ["A request body is required."] };
        var errors = SaleOrderValidator.Validate(request.CustomerType, request.Items);
        if (request.IdempotencyKey == Guid.Empty) errors["idempotencyKey"] = ["A non-empty idempotency key is required."];
        if (!Enum.IsDefined(request.PaymentMethod)) errors["paymentMethod"] = ["Specify Cash, Transfer or Card."];
        if (request.ExpectedTotal is null or < 0 || decimal.Round(request.ExpectedTotal.Value, 2) != request.ExpectedTotal.Value)
            errors["expectedTotal"] = ["Specify a non-negative expectedTotal with at most two decimal places."];
        if (request.CustomerId == Guid.Empty) errors["customerId"] = ["Specify an existing customer ID or omit it."];
        if (request.CustomerId is not null && request.CustomerUsername is not null)
            errors["customerUsername"] = ["Specify username only; customerId is retained for legacy pending requests."];
        if (request.CustomerUsername is not null && !string.IsNullOrWhiteSpace(request.CustomerUsername)
            && !Customer.TryNormalize(request.CustomerUsername, out _))
            errors["customerUsername"] = [Customer.UsernameRule];
        if (request.CustomerUsername?.Length > 256) errors["customerUsername"] = [Customer.UsernameRule];
        return errors;
    }
}
