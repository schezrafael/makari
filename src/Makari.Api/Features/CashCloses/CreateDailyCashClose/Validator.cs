namespace Makari.Api.Features.CashCloses.CreateDailyCashClose;

public static class CreateDailyCashCloseValidator
{
    private const decimal MaxAmount = 99999999999999999999999999.99m;

    public static Dictionary<string, string[]> Validate(CreateDailyCashCloseRequest? request, DateOnly today)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null) return new() { ["request"] = ["A request body is required."] };
        if (request.IdempotencyKey == Guid.Empty) errors["idempotencyKey"] = ["A non-empty idempotency key is required."];
        if (request.BusinessDate == DateOnly.MinValue)
            errors["businessDate"] = ["Specify a valid business date."];
        else if (request.BusinessDate == DateOnly.MaxValue || request.BusinessDate > today)
            errors["businessDate"] = ["A cash close cannot be created for a future business date."];
        if (!IsMoney(request.OpeningCash)) errors["openingCash"] = ["Specify a non-negative amount with at most two decimal places."];
        if (!IsMoney(request.CountedCash)) errors["countedCash"] = ["Specify a non-negative amount with at most two decimal places."];
        if (string.IsNullOrWhiteSpace(request.Responsible) || request.Responsible.Trim().Length > 100)
            errors["responsible"] = ["Specify a responsible person using 1 to 100 characters."];
        if (request.Observations?.Length > 2000) errors["observations"] = ["Observations may not exceed 2000 characters."];
        return errors;
    }

    private static bool IsMoney(decimal value) => value >= 0 && value <= MaxAmount
        && decimal.Round(value, 2) == value;
}
