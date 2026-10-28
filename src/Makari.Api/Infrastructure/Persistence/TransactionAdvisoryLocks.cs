using System.Buffers.Binary;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Makari.Api.Infrastructure.Persistence;

public static class TransactionAdvisoryLocks
{
    // The two-int PostgreSQL lock key is disjoint from the one-bigint sale-key lock space.
    private const int BusinessDateNamespace = 0x4D4B434C; // "MKCL"

    public static async Task AcquireSaleIdempotencyKeyAsync(
        MakariDbContext context, Guid idempotencyKey, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return;
        var lockId = BinaryPrimitives.ReadInt64BigEndian(SHA256.HashData(idempotencyKey.ToByteArray()));
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({lockId})", cancellationToken);
    }

    public static async Task AcquireBusinessDateAsync(
        MakariDbContext context, DateOnly businessDate, CancellationToken cancellationToken)
    {
        if (!context.Database.IsNpgsql()) return;
        await context.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock({BusinessDateNamespace}, {businessDate.DayNumber})", cancellationToken);
    }
}
