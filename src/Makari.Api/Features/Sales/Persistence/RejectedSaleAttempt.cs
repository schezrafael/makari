using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Makari.Api.Features.Sales.Persistence;

// A rejected key must never later create a sale, even if prices/availability change.
public sealed class RejectedSaleAttempt
{
    public Guid IdempotencyKey { get; init; }
    public string RequestFingerprint { get; init; } = null!;
    public decimal ExpectedTotal { get; init; }
    public decimal? CurrentTotal { get; init; }
    public string? ValidationErrorsJson { get; init; }
    public string? ConflictCode { get; init; }
    public DateOnly? ConflictBusinessDate { get; init; }
}

public sealed class RejectedSaleAttemptConfiguration : IEntityTypeConfiguration<RejectedSaleAttempt>
{
    public void Configure(EntityTypeBuilder<RejectedSaleAttempt> builder)
    {
        builder.ToTable("RejectedSaleAttempts");
        builder.HasKey(attempt => attempt.IdempotencyKey);
        builder.Property(attempt => attempt.IdempotencyKey).ValueGeneratedNever();
        builder.Property(attempt => attempt.RequestFingerprint).HasMaxLength(64).IsRequired();
        builder.Property(attempt => attempt.ExpectedTotal).HasPrecision(28, 2);
        builder.Property(attempt => attempt.CurrentTotal).HasPrecision(28, 2);
        builder.Property(attempt => attempt.ConflictCode).HasMaxLength(64);
    }
}
