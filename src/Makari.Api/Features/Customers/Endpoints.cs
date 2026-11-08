using Makari.Api.Infrastructure.Persistence;
using Makari.Api.Features.Customers.Domain;
using Microsoft.EntityFrameworkCore;
using Makari.Api.Features.Catalog.Domain;

namespace Makari.Api.Features.Customers;

public sealed record CustomerResponse(Guid Id, string Username, [property: System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter<CustomerType>))] CustomerType CustomerType = CustomerType.Visitor);
public sealed record UpdateCustomerTypeRequest(CustomerType? CustomerType, CustomerType? ExpectedCustomerType);

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/customers/frequent", FrequentAsync).WithName("FrequentCustomers").WithTags("Customers")
            .WithSummary("Most frequent identified buyers, ordered by sales count and latest purchase")
            .Produces<CustomerResponse[]>().ProducesValidationProblem();
        endpoints.MapGet("/api/customers", FindAsync).WithName("FindCustomer").WithTags("Customers");
        endpoints.MapPut("/api/customers/{id:guid}/classification", ClassifyAsync).WithName("ClassifyCustomer").WithTags("Customers");
        return endpoints;
    }

    private static async Task<IResult> FindAsync(string username, MakariDbContext db, CancellationToken ct)
    {
        if (!Customer.TryNormalize(username, out var clean)) return Results.ValidationProblem(new Dictionary<string, string[]> { ["username"] = [Customer.UsernameRule] });
        var normalized = clean.ToLowerInvariant();
        var customer = await db.Customers.AsNoTracking().SingleOrDefaultAsync(c => c.NormalizedUsername == normalized, ct);
        return customer is null ? Results.NotFound() : Results.Ok(new CustomerResponse(customer.Id, customer.Username, customer.CustomerType));
    }

    private static async Task<IResult> ClassifyAsync(Guid id, UpdateCustomerTypeRequest request, MakariDbContext db, CancellationToken ct)
    {
        if (request.CustomerType is null || !Enum.IsDefined(request.CustomerType.Value) || request.ExpectedCustomerType is null || !Enum.IsDefined(request.ExpectedCustomerType.Value))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["customerType"] = ["Specify Visitor or Local and the previously read expectedCustomerType."] });
        var changed = await db.Customers.Where(c => c.Id == id && c.CustomerType == request.ExpectedCustomerType)
            .ExecuteUpdateAsync(setters => setters.SetProperty(c => c.CustomerType, request.CustomerType.Value), ct);
        if (changed == 0) return await db.Customers.AnyAsync(c => c.Id == id, ct)
            ? Results.Conflict(new { code = "customer_changed", detail = "La clasificación cambió. Consulta de nuevo al comprador." }) : Results.NotFound();
        return Results.NoContent();
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
                    select new { customer.Id, customer.Username, customer.NormalizedUsername, customer.CustomerType, sale.CreatedAtUtc };
        if (db.Database.ProviderName == "Microsoft.EntityFrameworkCore.Sqlite")
        {
            // SQLite cannot aggregate/order DateTimeOffset. Production aggregates in PostgreSQL.
            var rows = await sales.ToListAsync(ct);
            return Results.Ok(rows.GroupBy(row => new { row.Id, row.Username, row.NormalizedUsername, row.CustomerType })
                .OrderByDescending(group => group.Count()).ThenByDescending(group => group.Max(row => row.CreatedAtUtc))
                .ThenBy(group => group.Key.NormalizedUsername, StringComparer.Ordinal).Take(count)
                .Select(group => new CustomerResponse(group.Key.Id, group.Key.Username, group.Key.CustomerType)).ToArray());
        }
        var result = await sales.GroupBy(row => new { row.Id, row.Username, row.NormalizedUsername, row.CustomerType })
            .Select(group => new { group.Key.Id, group.Key.Username, group.Key.NormalizedUsername, group.Key.CustomerType,
                Count = group.Count(), LastSale = group.Max(row => row.CreatedAtUtc) })
            .OrderByDescending(row => row.Count).ThenByDescending(row => row.LastSale).ThenBy(row => row.NormalizedUsername)
            .Take(count).Select(row => new CustomerResponse(row.Id, row.Username, row.CustomerType)).ToArrayAsync(ct);
        return Results.Ok(result);
    }
}
