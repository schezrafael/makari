using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Features.Sales.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Makari.Api.Features.Sales.CreateSale;

public sealed class CreateSaleHandler(MakariDbContext context, IConfiguration configuration)
{
    public async Task<CreateSaleOutcome> HandleAsync(
        CreateSaleRequest request, CancellationToken cancellationToken = default)
    {
        var fingerprint = CreateFingerprint(request);
        using var keyLock = await KeyedLock.AcquireAsync(request.IdempotencyKey, cancellationToken);

        var prior = await FindSaleAsync(request.IdempotencyKey, cancellationToken);
        if (prior is not null) return ResolveExisting(prior, fingerprint);

        var operatorName = configuration["Sales:RegisteredBy"] ?? "Andi";
        if (string.IsNullOrWhiteSpace(operatorName) || operatorName.Trim().Length > 100)
        {
            throw new InvalidOperationException("Sales:RegisteredBy must contain 1 to 100 characters.");
        }

        var sale = new Sale(request.CustomerType, request.PaymentMethod, operatorName,
            request.IdempotencyKey, fingerprint, DateTimeOffset.UtcNow);

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var variantIds = request.Items.Select(item => item.ProductVariantId).ToArray();
        var prices = await context.ProductVariants
            .AsNoTracking()
            .Where(variant => variantIds.Contains(variant.Id) && variant.IsActive && variant.Product.IsActive)
            .Select(variant => new
            {
                variant.Id,
                Price = variant.Prices
                    .Where(price => price.CustomerType == request.CustomerType)
                    .Select(price => (decimal?)price.Amount)
                    .SingleOrDefault()
            })
            .ToListAsync(cancellationToken);

        var priceByVariant = prices.ToDictionary(item => item.Id, item => item.Price);
        var unavailable = variantIds
            .Where(id => !priceByVariant.TryGetValue(id, out var price) || price is null)
            .Select(id => id.ToString())
            .ToArray();
        if (unavailable.Length > 0)
        {
            return CreateSaleOutcome.Invalid(new Dictionary<string, string[]>
            {
                ["items"] = [$"Variants are missing, inactive, or have no price for {request.CustomerType}: {string.Join(", ", unavailable)}."]
            });
        }

        foreach (var item in request.Items)
            sale.AddItem(item.ProductVariantId, item.Quantity, priceByVariant[item.ProductVariantId]!.Value);

        var uniqueConflict = false;
        context.Sales.Add(sale);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsIdempotencyUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            await transaction.DisposeAsync();
            uniqueConflict = true;
        }

        if (!uniqueConflict)
        {
            await transaction.CommitAsync(cancellationToken);
            return CreateSaleOutcome.Created(ToResponse(sale), isReplay: false);
        }

        // The transaction that lost the unique-key race is rolled back before rereading.
        context.ChangeTracker.Clear();
        var winner = await FindSaleAsync(request.IdempotencyKey, cancellationToken);
        if (winner is null) throw new InvalidOperationException("The idempotency key conflicted but its sale could not be read.");
        return ResolveExisting(winner, fingerprint);
    }

    private Task<Sale?> FindSaleAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
        context.Sales.AsNoTracking().Include(sale => sale.Items)
            .SingleOrDefaultAsync(sale => sale.IdempotencyKey == idempotencyKey, cancellationToken);

    private static CreateSaleOutcome ResolveExisting(Sale sale, string fingerprint) =>
        sale.RequestFingerprint == fingerprint
            ? CreateSaleOutcome.Created(ToResponse(sale), isReplay: true)
            : CreateSaleOutcome.ConflictResult("This idempotency key has already been used with different sale contents.");

    private static CreateSaleResponse ToResponse(Sale sale) => new(
        sale.Id, sale.CreatedAtUtc, sale.CustomerType, sale.PaymentMethod, sale.Total,
        sale.RegisteredBy, sale.IdempotencyKey,
        sale.Items.OrderBy(item => item.ProductVariantId)
            .Select(item => new CreateSaleItemResponse(item.Id, item.ProductVariantId,
                item.Quantity, item.UnitPrice, item.Subtotal)).ToArray());

    private static string CreateFingerprint(CreateSaleRequest request)
    {
        var canonicalItems = request.Items.OrderBy(item => item.ProductVariantId)
            .Select(item => $"{item.ProductVariantId:N}:{item.Quantity.ToString(CultureInfo.InvariantCulture)}");
        var canonical = $"{(int)request.CustomerType}|{(int)request.PaymentMethod}|{string.Join(';', canonicalItems)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    private static bool IsIdempotencyUniqueViolation(DbUpdateException exception)
    {
        if (exception.InnerException is PostgresException postgres)
            return postgres.SqlState == PostgresErrorCodes.UniqueViolation &&
                   postgres.ConstraintName == "IX_Sales_IdempotencyKey";
        return false;
    }

    private sealed class LockEntry
    {
        public SemaphoreSlim Semaphore { get; } = new(1, 1);
        public int References { get; set; } = 1;
    }

    // Process-level coordination keeps SQLite and same-process retries deterministic.
    // The database unique index remains the cross-process authority.
    private static class KeyedLock
    {
        private static readonly object Sync = new();
        private static readonly Dictionary<Guid, LockEntry> Entries = [];

        public static async ValueTask<IDisposable> AcquireAsync(Guid key, CancellationToken cancellationToken)
        {
            LockEntry entry;
            lock (Sync)
            {
                if (Entries.TryGetValue(key, out var existing))
                {
                    entry = existing;
                    entry.References++;
                }
                else
                    entry = new LockEntry();
                Entries[key] = entry;
            }

            try
            {
                await entry.Semaphore.WaitAsync(cancellationToken);
                return new Lease(key, entry);
            }
            catch
            {
                RemoveReference(key, entry);
                throw;
            }
        }

        private static void RemoveReference(Guid key, LockEntry entry)
        {
            lock (Sync)
            {
                if (!Entries.TryGetValue(key, out var current) || !ReferenceEquals(current, entry)) return;
                if (current.References == 1)
                {
                    Entries.Remove(key);
                    current.Semaphore.Dispose();
                }
                else current.References--;
            }
        }

        private sealed class Lease(Guid key, LockEntry entry) : IDisposable
        {
            private int _released;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) != 0) return;
                entry.Semaphore.Release();
                RemoveReference(key, entry);
            }
        }
    }
}

public sealed record CreateSaleOutcome(
    CreateSaleResponse? Response,
    bool IsReplay,
    IReadOnlyDictionary<string, string[]>? Errors,
    string? Conflict)
{
    public static CreateSaleOutcome Created(CreateSaleResponse response, bool isReplay) =>
        new(response, isReplay, null, null);

    public static CreateSaleOutcome Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(null, false, errors, null);

    public static CreateSaleOutcome ConflictResult(string detail) =>
        new(null, false, null, detail);
}
