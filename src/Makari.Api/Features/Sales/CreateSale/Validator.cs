using Makari.Api.Features.Customers.Domain;
using Makari.Api.Features.Sales.Domain;

namespace Makari.Api.Features.Sales.CreateSale;

public static class CreateSaleValidator
{
    public static Dictionary<string, string[]> Validate(CreateSaleRequest? request)
    {
        if (request is null) return new() { ["request"] = ["A request body is required."] };
        var errors = SaleOrderValidator.Validate(request.CustomerType ?? request.PricingType, request.Items);
        if (request.IdempotencyKey == Guid.Empty) errors["idempotencyKey"] = ["A non-empty idempotency key is required."];
        if (!Enum.IsDefined(request.EffectiveCondition)) errors["saleCondition"] = ["Specify Normal, LocalDiscount, Courtesy, Staff or Birthday."];
        if (request.PaymentMethod is not null && !Enum.IsDefined(request.PaymentMethod.Value)) errors["paymentMethod"] = ["Specify Cash, Transfer or Card."];
        if (!request.Legacy && ((request.ExpectedTotal == 0 && request.PaymentMethod is not null) || (request.ExpectedTotal > 0 && request.PaymentMethod is null)))
            errors["paymentMethod"] = ["Use no payment method for zero totals; otherwise specify a payment method."];
        if (request.EffectiveCondition.IsFree() && (request.ExpectedTotal != 0 || request.PaymentMethod is not null))
            errors["saleCondition"] = ["Free operations require expectedTotal zero and no payment method."];
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
