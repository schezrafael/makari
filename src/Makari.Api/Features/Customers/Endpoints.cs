using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Features.Customers.Domain;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Customers;

public sealed record CustomerResponse(Guid Id, string Username);

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customers/frequent", FrequentAsync).WithName("FrequentCustomers").WithTags("Customers")
            .WithSummary("Most frequent identified buyers, ordered by sales count and latest purchase")
            .Produces<CustomerResponse[]>().ProducesValidationProblem();
        return endpoints;
    }

    private static async Task<IResult> FrequentAsync(int? limit, MakariDbContext db, CancellationToken ct)
    {
        var count = limit ?? 5;
        if (count is < 1 or > 5) return Results.ValidationProblem(
            new Dictionary<string, string[]> { ["limit"] = ["Use a limit between 1 and 5."] });
        var sales = from sale in db.Sales.AsNoTracking()
                    join customer in db.Customers.AsNoTracking() on sale.CustomerId equals customer.Id
                    where customer.NormalizedUsername != "visitante" && customer.NormalizedUsername != "local"
                        && customer.Id != Customer.VisitorId && customer.Id != Customer.LocalId
                    select new { customer.Id, customer.Username, customer.NormalizedUsername, sale.CreatedAtUtc };
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite cannot aggregate/order DateTimeOffset. Production aggregates in PostgreSQL.
            var rows = await sales.ToListAsync(ct);
            return Results.Ok(rows.GroupBy(row => new { row.Id, row.Username, row.NormalizedUsername })
                .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Max(row => row.CreatedAtUtc))
                .ThenBy(group => group.Key.NormalizedUsername, StringComparer.Ordinal).Take(count)
                .Select(group => new CustomerResponse(group.Key.Id, group.Key.Username)).ToArray());
        }
        var result = await sales.GroupBy(row => new { row.Id, row.Username, row.NormalizedUsername })
            .Select(group => new { group.Key.Id, group.Key.Username, group.Key.NormalizedUsername,
                Count = group.Count(), LastSale = group.Max(row => row.CreatedAtUtc) })
            .OrderByDescending(row => row.Count).ThenByDescending(row => row.LastSale).ThenBy(row => row.NormalizedUsername)
            .Take(count).Select(row => new CustomerResponse(row.Id, row.Username)).ToArrayAsync(ct);
        return Results.Ok(result);
    }
}
