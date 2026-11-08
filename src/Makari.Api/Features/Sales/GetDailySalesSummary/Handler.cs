using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Sales.GetDailySalesSummary;

public sealed class GetDailySalesSummaryHandler
{
    private readonly MakariDbContext _context;
    private readonly TimeZoneInfo _timeZone;

    public GetDailySalesSummaryHandler(MakariDbContext context, IConfiguration configuration)
    {
        _context = context;
        var timeZoneId = configuration["Business:TimeZoneId"] ?? "America/Mexico_City";
        _timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, _timeZone).DateTime);

    public async Task<GetDailySalesSummaryResponse> HandleAsync(
        DateOnly date, CancellationToken cancellationToken = default)
    {
        var startUtc = LocalMidnightToUtc(date);
        var endUtc = LocalMidnightToUtc(date.AddDays(1));

        // SQLite (used by isolated integration tests) cannot translate ordering comparisons
        // on DateTimeOffset. PostgreSQL executes the aggregate projections below in SQL.
        if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
            return await AddConditionsAsync(await HandleSqliteAsync(date, startUtc, endUtc, cancellationToken), startUtc, endUtc, cancellationToken);

        var sales = _context.Sales.AsNoTracking()
            .Where(sale => sale.CreatedAtUtc >= startUtc && sale.CreatedAtUtc < endUtc);

        var totals = await sales
            .GroupBy(sale => 1)
            .Select(group => new
            {
                Count = group.Count(),
                Revenue = group.Sum(sale => sale.Total),
                Cash = group.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => (decimal?)sale.Total),
                Transfer = group.Where(sale => sale.PaymentMethod == PaymentMethod.Transfer).Sum(sale => (decimal?)sale.Total),
                Card = group.Where(sale => sale.PaymentMethod == PaymentMethod.Card).Sum(sale => (decimal?)sale.Total)
            })
            .SingleOrDefaultAsync(cancellationToken);

        var aggregatedVariants = await (
                from item in _context.SaleItems.AsNoTracking()
                join sale in _context.Sales.AsNoTracking() on item.SaleId equals sale.Id
                join variant in _context.ProductVariants.AsNoTracking() on item.ProductVariantId equals variant.Id
                join product in _context.Products.AsNoTracking() on variant.ProductId equals product.Id
                where sale.CreatedAtUtc >= startUtc && sale.CreatedAtUtc < endUtc
                group item by new { variant.Id, ProductName = product.Name, VariantName = variant.Name } into grouped
                select new
                {
                    ProductVariantId = grouped.Key.Id,
                    grouped.Key.ProductName,
                    grouped.Key.VariantName,
                    Quantity = grouped.Sum(item => (long)item.Quantity),
                    Revenue = grouped.Sum(item => item.Subtotal)
                })
            .ToListAsync(cancellationToken);
        var variants = aggregatedVariants
            .OrderByDescending(item => item.Revenue)
            .ThenBy(item => item.ProductName)
            .ThenBy(item => item.VariantName)
            .Select(item => new VariantDailySalesSummary(
                item.ProductVariantId, item.ProductName, item.VariantName, item.Quantity, item.Revenue))
            .ToArray();

        return await AddConditionsAsync(new GetDailySalesSummaryResponse(
            date,
            _timeZone.Id,
            totals?.Count ?? 0,
            totals?.Revenue ?? 0m,
            totals?.Cash ?? 0m,
            totals?.Transfer ?? 0m,
            totals?.Card ?? 0m,
            variants.Sum(item => item.Quantity),
            variants), startUtc, endUtc, cancellationToken);
    }

    private async Task<GetDailySalesSummaryResponse> HandleSqliteAsync(
        DateOnly date, DateTimeOffset startUtc, DateTimeOffset endUtc, CancellationToken cancellationToken)
    {
        var sales = (await _context.Sales.AsNoTracking()
                .Select(sale => new { sale.Id, sale.CreatedAtUtc, sale.PaymentMethod, sale.Total })
                .ToListAsync(cancellationToken))
            .Where(sale => sale.CreatedAtUtc >= startUtc && sale.CreatedAtUtc < endUtc)
            .ToArray();
        var saleIds = sales.Select(sale => sale.Id).ToHashSet();
        var lineData = await (
                from item in _context.SaleItems.AsNoTracking()
                join variant in _context.ProductVariants.AsNoTracking() on item.ProductVariantId equals variant.Id
                join product in _context.Products.AsNoTracking() on variant.ProductId equals product.Id
                select new { item.SaleId, item.ProductVariantId, item.Quantity, item.Subtotal, ProductName = product.Name, VariantName = variant.Name })
            .ToListAsync(cancellationToken);
        var variants = lineData.Where(item => saleIds.Contains(item.SaleId))
            .GroupBy(item => new { item.ProductVariantId, item.ProductName, item.VariantName })
            .Select(group => new VariantDailySalesSummary(group.Key.ProductVariantId, group.Key.ProductName,
                group.Key.VariantName, group.Sum(item => (long)item.Quantity), group.Sum(item => item.Subtotal)))
            .OrderByDescending(item => item.Revenue).ThenBy(item => item.ProductName).ThenBy(item => item.VariantName)
            .ToArray();
        return new GetDailySalesSummaryResponse(date, _timeZone.Id, sales.Length, sales.Sum(sale => sale.Total),
            sales.Where(sale => sale.PaymentMethod == PaymentMethod.Cash).Sum(sale => sale.Total),
            sales.Where(sale => sale.PaymentMethod == PaymentMethod.Transfer).Sum(sale => sale.Total),
            sales.Where(sale => sale.PaymentMethod == PaymentMethod.Card).Sum(sale => sale.Total),
            variants.Sum(item => item.Quantity), variants);
    }

    private async Task<GetDailySalesSummaryResponse> AddConditionsAsync(GetDailySalesSummaryResponse response,
        DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var query = from item in _context.SaleItems.AsNoTracking()
                    join sale in _context.Sales.AsNoTracking() on item.SaleId equals sale.Id
                    select new { sale.Id, sale.CreatedAtUtc, sale.SaleCondition, item.Quantity, item.UnitPrice, item.Subtotal };
        ConditionDailySummary[] conditions;
        long paid, free;
        if (_context.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            var rows = (await query.ToListAsync(ct)).Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end).ToArray();
            paid = rows.Where(x => x.UnitPrice > 0).Sum(x => (long)x.Quantity);
            free = rows.Where(x => x.UnitPrice == 0).Sum(x => (long)x.Quantity);
            conditions = rows.GroupBy(x => x.SaleCondition).Select(g => new ConditionDailySummary(g.Key, g.Select(x => x.Id).Distinct().Count(), g.Sum(x => (long)x.Quantity), g.Sum(x => x.Subtotal))).ToArray();
        }
        else
        {
            query = query.Where(x => x.CreatedAtUtc >= start && x.CreatedAtUtc < end);
            var units = await query.GroupBy(x => x.UnitPrice > 0).Select(g => new { Paid = g.Key, Units = g.Sum(x => (long)x.Quantity) }).ToListAsync(ct);
            paid = units.Where(x => x.Paid).Sum(x => x.Units);
            free = units.Where(x => !x.Paid).Sum(x => x.Units);
            conditions = await query.GroupBy(x => x.SaleCondition).Select(g => new ConditionDailySummary(g.Key, g.Select(x => x.Id).Distinct().Count(), g.Sum(x => (long)x.Quantity), g.Sum(x => x.Subtotal))).ToArrayAsync(ct);
        }
        return response with { PaidUnits = paid, FreeUnits = free, Conditions = Enum.GetValues<SaleCondition>().Select(c => conditions.SingleOrDefault(x => x.SaleCondition == c) ?? new(c, 0, 0, 0)).ToArray() };
    }

    private DateTimeOffset LocalMidnightToUtc(DateOnly date)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        while (_timeZone.IsInvalidTime(local)) local = local.AddMinutes(1);

        // At an ambiguous midnight, the larger offset is the first occurrence in UTC.
        var offset = _timeZone.IsAmbiguousTime(local)
            ? _timeZone.GetAmbiguousTimeOffsets(local).Max()
            : _timeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
