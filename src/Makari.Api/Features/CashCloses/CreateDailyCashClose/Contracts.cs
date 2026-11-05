namespace Makari.Api.Features.CashCloses.CreateDailyCashClose;

public sealed record CreateDailyCashCloseRequest(
    Guid IdempotencyKey,
    DateOnly BusinessDate,
    decimal OpeningCash,
    decimal CountedCash,
    string Responsible,
    string? Observations);

public sealed record DailyCashCloseResponse(
    Guid Id,
    DateOnly BusinessDate,
    DateTimeOffset ClosedAtUtc,
    Guid IdempotencyKey,
    decimal OpeningCash,
    decimal CashSales,
    decimal TransferSales,
    decimal CardSales,
    decimal TotalRevenue,
    int SalesCount,
    decimal ExpectedCash,
    decimal CountedCash,
    decimal Difference,
    string Responsible,
    string? Observations);

public sealed record DailyCashCloseOutcome(
    DailyCashCloseResponse? Response,
    bool IsReplay,
    IReadOnlyDictionary<string, string[]>? Errors,
    string? ConflictCode,
    string? ConflictDetail,
    Guid? ExistingCloseId = null)
{
    public static DailyCashCloseOutcome Created(DailyCashCloseResponse response, bool replay = false) =>
        new(response, replay, null, null, null);

    public static DailyCashCloseOutcome Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(null, false, errors, null, null);

    public static DailyCashCloseOutcome Conflict(string code, string detail, Guid? closeId = null) =>
        new(null, false, null, code, detail, closeId);
}
