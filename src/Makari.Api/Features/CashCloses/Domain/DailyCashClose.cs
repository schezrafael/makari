namespace Makari.Api.Features.CashCloses.Domain;

public sealed class DailyCashClose
{
    private DailyCashClose() { }

    public DailyCashClose(DateOnly businessDate, Guid idempotencyKey, string requestFingerprint,
        decimal openingCash, decimal cashSales, decimal transferSales, decimal totalRevenue, int salesCount,
        decimal expectedCash, decimal countedCash, decimal difference, string responsible,
        string? observations, DateTimeOffset closedAtUtc)
    {
        if (businessDate == DateOnly.MaxValue) throw new ArgumentOutOfRangeException(nameof(businessDate));
        if (idempotencyKey == Guid.Empty) throw new ArgumentException("An idempotency key is required.", nameof(idempotencyKey));
        ArgumentException.ThrowIfNullOrWhiteSpace(requestFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(responsible);
        if (salesCount < 0) throw new ArgumentOutOfRangeException(nameof(salesCount));

        Id = Guid.NewGuid();
        BusinessDate = businessDate;
        IdempotencyKey = idempotencyKey;
        RequestFingerprint = requestFingerprint;
        OpeningCash = openingCash;
        CashSales = cashSales;
        TransferSales = transferSales;
        TotalRevenue = totalRevenue;
        SalesCount = salesCount;
        ExpectedCash = expectedCash;
        CountedCash = countedCash;
        Difference = difference;
        Responsible = responsible.Trim();
        Observations = string.IsNullOrWhiteSpace(observations) ? null : observations.Trim();
        ClosedAtUtc = closedAtUtc.ToUniversalTime();
    }

    public Guid Id { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DateTimeOffset ClosedAtUtc { get; private set; }
    public Guid IdempotencyKey { get; private set; }
    public string RequestFingerprint { get; private set; } = null!;
    public decimal OpeningCash { get; private set; }
    public decimal CashSales { get; private set; }
    public decimal TransferSales { get; private set; }
    public decimal TotalRevenue { get; private set; }
    public int SalesCount { get; private set; }
    public decimal ExpectedCash { get; private set; }
    public decimal CountedCash { get; private set; }
    public decimal Difference { get; private set; }
    public string Responsible { get; private set; } = null!;
    public string? Observations { get; private set; }
}
