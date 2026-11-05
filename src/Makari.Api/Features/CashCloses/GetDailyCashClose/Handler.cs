using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.CashCloses.GetDailyCashClose;

public sealed class GetDailyCashCloseHandler(MakariDbContext context, IConfiguration configuration)
{
    private readonly TimeZoneInfo _businessZone = TimeZoneInfo.FindSystemTimeZoneById(
        configuration["Business:TimeZoneId"] ?? "America/Mexico_City");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _businessZone).DateTime);

    public Task<DailyCashCloseResponse?> ByDateAsync(DateOnly date, CancellationToken cancellationToken) =>
        context.DailyCashCloses.AsNoTracking().Where(close => close.BusinessDate == date)
            .Select(close => new DailyCashCloseResponse(close.Id, close.BusinessDate, close.ClosedAtUtc,
                close.IdempotencyKey, close.OpeningCash, close.CashSales, close.TransferSales, close.CardSales,
                close.TotalRevenue, close.SalesCount, close.ExpectedCash, close.CountedCash,
                close.Difference, close.Responsible, close.Observations))
            .SingleOrDefaultAsync(cancellationToken);
}
