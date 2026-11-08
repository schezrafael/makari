namespace Makari.Api.Features.Sales.GetDailySalesSummary;

public sealed record GetDailySalesSummaryResponse(
    DateOnly Date,
    string TimeZoneId,
    int SalesCount,
    decimal TotalRevenue,
    decimal CashRevenue,
    decimal TransferRevenue,
    decimal CardRevenue,
    long UnitsSold,
    IReadOnlyList<VariantDailySalesSummary> Variants)
{
    public long UnitsRegistered => UnitsSold;
    public long PaidUnits { get; init; }
    public long FreeUnits { get; init; }
    public IReadOnlyList<ConditionDailySummary> Conditions { get; init; } = [];
}
public sealed record ConditionDailySummary([property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<Makari.Api.Features.Sales.Domain.SaleCondition>))] Makari.Api.Features.Sales.Domain.SaleCondition SaleCondition, int Operations, long Units, decimal Revenue);

public sealed record VariantDailySalesSummary(
    Guid ProductVariantId,
    string ProductName,
    string VariantName,
    long Quantity,
    decimal Revenue);
