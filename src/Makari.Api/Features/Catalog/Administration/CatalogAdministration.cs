using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.Administration;

public sealed record ProductResponse(Guid Id, string Name, string Category, bool IsActive, Guid Version, VariantResponse[] Variants)
{
    public static ProductResponse From(Product p) => new(p.Id, p.Name, p.Category, p.IsActive, p.Version,
        p.Variants.OrderBy(v => v.Name).ThenBy(v => v.SizeMl).Select(v => new VariantResponse(v.Id, v.Name, v.SizeMl, v.IsActive,
            v.Prices.SingleOrDefault(x => x.CustomerType == CustomerType.Visitor)?.Amount,
            v.Prices.SingleOrDefault(x => x.CustomerType == CustomerType.Local)?.Amount)).ToArray());
}
public sealed record VariantResponse(Guid Id, string Name, int SizeMl, bool IsActive, decimal? VisitorPrice, decimal? LocalPrice);

internal static class CatalogAdministration
{
    public static bool ValidName(string? name) => !string.IsNullOrWhiteSpace(name) && name.Trim().Length <= 100;
    public static bool ValidPrice(decimal? price) => price is >= 0 and <= 9_999_999_999.99m && decimal.Round(price.Value, 2) == price;
    public static IResult Invalid(string field, string message) => Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });
    public static IResult Conflict(string code, string detail) => Results.Conflict(new { code, detail });
    public static IResult Stale() => Conflict("catalog_changed", "El producto cambió. Recarga el catálogo y revisa los cambios antes de guardar.");
    public static Task<Product?> Load(MakariDbContext db, Guid id, CancellationToken ct) => db.Products
        .Include(p => p.Variants).ThenInclude(v => v.Prices).SingleOrDefaultAsync(p => p.Id == id, ct);
    public static async Task<IResult> Save(MakariDbContext db, Product product, CancellationToken ct)
    {
        product.Revise();
        await db.SaveChangesAsync(ct);
        return Results.Ok(ProductResponse.From(product));
    }
    public static bool DuplicateVariant(Product product, string name, int sizeMl, Guid? except = null) =>
        product.Variants.Any(v => v.Id != except && v.SizeMl == sizeMl && string.Equals(v.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

// One small catalog: serialize administrative writes across processes, including duplicate checks.
// Shared checkout locks order completed sales before subsequent catalog edits.
internal sealed class CatalogWriteFilter : IEndpointFilter
{
    private static readonly SemaphoreSlim LocalGate = new(1, 1);
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocation, EndpointFilterDelegate next)
    {
        var ct = invocation.HttpContext.RequestAborted;
        var db = invocation.HttpContext.RequestServices.GetRequiredService<MakariDbContext>();
        await LocalGate.WaitAsync(ct);
        try
        {
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            await TransactionAdvisoryLocks.AcquireCatalogAsync(db, false, ct);
            try
            {
                var result = await next(invocation);
                await tx.CommitAsync(ct);
                return result;
            }
            catch (DbUpdateConcurrencyException) { return CatalogAdministration.Stale(); }
        }
        finally { LocalGate.Release(); }
    }
}
