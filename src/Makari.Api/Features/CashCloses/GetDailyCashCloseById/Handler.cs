using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Features.CashCloses.CreateDailyCashClose;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.CashCloses.GetDailyCashCloseById;

public sealed class GetDailyCashCloseByIdHandler(MakariDbContext context)
{
    public Task<DailyCashCloseResponse?> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        context.DailyCashCloses.AsNoTracking().Where(close => close.Id == id)
            .Select(close => new DailyCashCloseResponse(close.Id, close.BusinessDate, close.ClosedAtUtc,
                close.IdempotencyKey, close.OpeningCash, close.CashSales, close.TransferSales, close.CardSales,
                close.TotalRevenue, close.SalesCount, close.ExpectedCash, close.CountedCash,
                close.Difference, close.Responsible, close.Observations))
            .SingleOrDefaultAsync(cancellationToken);
}
