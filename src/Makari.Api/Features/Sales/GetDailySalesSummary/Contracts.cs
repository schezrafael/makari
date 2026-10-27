namespace Makari.Api.Features.Sales.GetDailySalesSummary;

public sealed record GetDailySalesSummaryResponse(
    DateOnly Date,
    string TimeZoneId,
    int SalesCount,
    decimal TotalRevenue,
    decimal CashRevenue,
    decimal TransferRevenue,
    long UnitsSold,
    IReadOnlyList<VariantDailySalesSummary> Variants);

public sealed record VariantDailySalesSummary(
    Guid ProductVariantId,
    string ProductName,
    string VariantName,
    long Quantity,
    decimal Revenue);
