using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Makari.Api.Features.CashCloses.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Features.Sales.Persistence;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Makari.Api.Features.CashCloses.CreateDailyCashClose;

public sealed class CreateDailyCashCloseHandler(MakariDbContext context, IConfiguration configuration)
{
    private readonly TimeZoneInfo _businessZone = TimeZoneInfo.FindSystemTimeZoneById(
        configuration["Business:TimeZoneId"] ?? "America/Mexico_City");

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _businessZone).DateTime);

    public async Task<DailyCashCloseOutcome> HandleAsync(
        CreateDailyCashCloseRequest request, CancellationToken cancellationToken = default)
    {
        var fingerprint = CreateFingerprint(request);
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        await TransactionAdvisoryLocks.AcquireSaleIdempotencyKeyAsync(context, request.IdempotencyKey, cancellationToken);

        var prior = await context.DailyCashCloses.AsNoTracking()
            .SingleOrDefaultAsync(close => close.IdempotencyKey == request.IdempotencyKey, cancellationToken);
        if (prior is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return prior.RequestFingerprint == fingerprint
                ? DailyCashCloseOutcome.Created(ToResponse(prior), replay: true)
                : DailyCashCloseOutcome.Conflict("idempotency_conflict", "This idempotency key has already been used with different close contents.");
        }

        await TransactionAdvisoryLocks.AcquireBusinessDateAsync(context, request.BusinessDate, cancellationToken);
        var existing = await context.DailyCashCloses.AsNoTracking()
            .SingleOrDefaultAsync(close => close.BusinessDate == request.BusinessDate, cancellationToken);
        if (existing is not null)
        {
            await transaction.CommitAsync(cancellationToken);
            return DailyCashCloseOutcome.Conflict("cash_close_exists",
                "This business date already has a confirmed cash close.", existing.Id);
        }

        var (startUtc, endUtc) = DayBounds(request.BusinessDate);
        var totals = await GetSalesTotalsAsync(startUtc, endUtc, cancellationToken);
        var expectedCash = checked(request.OpeningCash + totals.CashSales);
        var difference = checked(request.CountedCash - expectedCash);
        var close = new DailyCashClose(request.BusinessDate, request.IdempotencyKey, fingerprint,
            request.OpeningCash, totals.CashSales, totals.TransferSales, totals.CardSales, totals.TotalRevenue, totals.SalesCount,
            expectedCash, request.CountedCash, difference, request.Responsible,
            request.Observations, DateTimeOffset.UtcNow);
        context.DailyCashCloses.Add(close);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            context.ChangeTracker.Clear();
            var winner = await context.DailyCashCloses.AsNoTracking()
                .SingleOrDefaultAsync(row => row.BusinessDate == request.BusinessDate, cancellationToken);
            if (winner is not null)
                return winner.IdempotencyKey == request.IdempotencyKey && winner.RequestFingerprint == fingerprint
                    ? DailyCashCloseOutcome.Created(ToResponse(winner), replay: true)
                    : DailyCashCloseOutcome.Conflict("cash_close_exists",
                        "This business date already has a confirmed cash close.", winner.Id);

            winner = await context.DailyCashCloses.AsNoTracking()
                .SingleOrDefaultAsync(row => row.IdempotencyKey == request.IdempotencyKey, cancellationToken);
            if (winner is not null && winner.RequestFingerprint == fingerprint)
                return DailyCashCloseOutcome.Created(ToResponse(winner), replay: true);
            return DailyCashCloseOutcome.Conflict("idempotency_conflict",
                "This idempotency key has already been used with different close contents.");
        }

        await transaction.CommitAsync(cancellationToken);
        return DailyCashCloseOutcome.Created(ToResponse(close));
    }

    private async Task<SalesTotals> GetSalesTotalsAsync(
        DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        var query = context.Sales.AsNoTracking()
            .Where(sale => sale.CreatedAtUtc >= startUtc && sale.CreatedAtUtc < endUtc);
        if (context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var sales = (await context.Sales.AsNoTracking()
                    .Select(sale => new { sale.CreatedAtUtc, sale.PaymentMethod, sale.Total })
                    .ToListAsync(cancellationToken))
                .Where(sale => sale.CreatedAtUtc >= startUtc && sale.CreatedAtUtc < endUtc)
                .ToArray();
            return new SalesTotals(sales.Length, sales.Sum(sale => sale.Total),
                sales.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => sale.Total),
                sales.Where(sale => sale.PaymentMethod == PaymentMethod.Transfer).Sum(sale => sale.Total),
                sales.Where(sale => sale.PaymentMethod == PaymentMethod.Card).Sum(sale => sale.Total));
        }

        var totals = await query.GroupBy(_ => 1).Select(group => new
        {
            Count = group.Count(),
            Total = group.Sum(sale => sale.Total),
            Cash = group.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => (decimal?)sale.Total),
            Transfer = group.Where(sale => sale.PaymentMethod == PaymentMethod.Transfer).Sum(sale => (decimal?)sale.Total),
            Card = group.Where(sale => sale.PaymentMethod == PaymentMethod.Card).Sum(sale => (decimal?)sale.Total)
        }).SingleOrDefaultAsync(cancellationToken);
        return totals is null
            ? new SalesTotals(0, 0m, 0m, 0m, 0m)
            : new SalesTotals(totals.Count, totals.Total, totals.Cash ?? 0m, totals.Transfer ?? 0m, totals.Card ?? 0m);
    }

    private (DateTimeOffset StartUtc, DateTimeOffset EndUtc) DayBounds(DateOnly date) =>
        (LocalMidnightToUtc(date), LocalMidnightToUtc(date.AddDays(1)));

    private DateTimeOffset LocalMidnightToUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (_businessZone.IsInvalidTime(local)) local = local.AddMinutes(1);
        var offset = _businessZone.IsAmbiguousTime(local)
            ? _businessZone.GetAmbiguousTimeOffsets(local).Max()
            : _businessZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }

    private static string CreateFingerprint(CreateDailyCashCloseRequest request)
    {
        var canonical = JsonSerializer.Serialize(new
        {
            businessDate = request.BusinessDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            openingCash = request.OpeningCash.ToString("G29", CultureInfo.InvariantCulture),
            countedCash = request.CountedCash.ToString("G29", CultureInfo.InvariantCulture),
            responsible = request.Responsible.Trim(),
            observations = request.Observations?.Trim()
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    internal static DailyCashCloseResponse ToResponse(DailyCashClose close) => new(
        close.Id, close.BusinessDate, close.ClosedAtUtc, close.IdempotencyKey, close.OpeningCash,
        close.CashSales, close.TransferSales, close.CardSales, close.TotalRevenue, close.SalesCount, close.ExpectedCash,
        close.CountedCash, close.Difference, close.Responsible, close.Observations);

    private sealed record SalesTotals(int SalesCount, decimal TotalRevenue, decimal CashSales, decimal TransferSales, decimal CardSales);
}
