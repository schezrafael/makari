using Makari.Api.Features.Catalog.Domain;
using Makari.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Features.Catalog.Persistence;

public static class CatalogSeeder
{
    public static readonly Guid ColdBrewId = Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000001");
    public static readonly Guid NaturalId = Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000002");
    public static readonly Guid CitricId = Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000003");

    private static readonly VariantSeed[] Variants =
    [
        new(NaturalId, "Natural",
        [
            new(Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000004"), CustomerType.Visitor, 60m),
            new(Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000005"), CustomerType.Local, 50m)
        ]),
        new(CitricId, "Cítrico",
        [
            new(Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000006"), CustomerType.Visitor, 75m),
            new(Guid.Parse("1ea4ad9a-f40c-4c65-9fab-000000000007"), CustomerType.Local, 60m)
        ])
    ];

    public static async Task SeedAsync(MakariDbContext context, CancellationToken cancellationToken = default)
    {
        if ((await context.Database.GetPendingMigrationsAsync(cancellationToken)).Any())
        {
            throw new InvalidOperationException("Apply the reviewed migrations to the authorized database before seeding.");
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Serialize initializers and competing catalog writes while resolving names.
        // This command does not create tables or apply migrations.
        await context.Database.ExecuteSqlRawAsync(
            """LOCK TABLE "Products", "ProductVariants", "ProductPrices" IN SHARE ROW EXCLUSIVE MODE""",
            cancellationToken);
        var products = await context.Products
            .Include(product => product.Variants).ThenInclude(variant => variant.Prices)
            .ToListAsync(cancellationToken);

        AddMissingEntries(context, products);
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    // Stages inserts only. The caller supplies the complete, tracked catalog.
    // Kept separate from database I/O to test matching and preservation rules.
    public static void AddMissingEntries(MakariDbContext context, IReadOnlyCollection<Product> products)
    {
        var product = SingleMatch(products.Where(product =>
            product.Id == ColdBrewId || SameName(product.Name, "Cold Brew")), "Cold Brew");
        if (product is null)
        {
            product = new Product("Cold Brew", "Café");
            context.Entry(product).Property(p => p.Id).CurrentValue = ColdBrewId;
            context.Products.Add(product);
        }

        var allVariants = products.SelectMany(product => product.Variants).ToArray();
        var allPrices = allVariants.SelectMany(variant => variant.Prices).ToArray();
        foreach (var seed in Variants)
        {
            if (allVariants.Any(variant => variant.Id == seed.Id && variant.ProductId != product.Id))
            {
                throw new InvalidOperationException($"Seed variant ID for {seed.Name} belongs to another product.");
            }

            var variant = SingleMatch(product.Variants.Where(variant =>
                variant.Id == seed.Id || (SameName(variant.Name, seed.Name) && variant.SizeMl == 500)), seed.Name);
            if (variant is null)
            {
                variant = product.AddVariant(seed.Name, 500);
                context.Entry(variant).Property(v => v.Id).CurrentValue = seed.Id;
                context.ProductVariants.Add(variant);
            }

            foreach (var priceSeed in seed.Prices)
            {
                if (allPrices.Any(price => price.Id == priceSeed.Id &&
                    (price.ProductVariantId != variant.Id || price.CustomerType != priceSeed.CustomerType)))
                {
                    throw new InvalidOperationException($"Seed price ID for {seed.Name} conflicts with an existing price.");
                }

                // Existing prices, IDs, names and active states belong to the operator.
                if (variant.Prices.Any(price => price.CustomerType == priceSeed.CustomerType))
                {
                    continue;
                }

                var price = variant.AddPrice(priceSeed.CustomerType, priceSeed.Amount);
                context.Entry(price).Property(p => p.Id).CurrentValue = priceSeed.Id;
                context.ProductPrices.Add(price);
            }
        }
    }

    private static bool SameName(string left, string right) =>
        string.Equals(left.Trim(), right, StringComparison.OrdinalIgnoreCase);

    private static T? SingleMatch<T>(IEnumerable<T> candidates, string name) where T : class
    {
        var matches = candidates.Take(2).ToArray();
        return matches.Length switch
        {
            0 => null,
            1 => matches[0],
            _ => throw new InvalidOperationException($"Ambiguous existing catalog entry: {name}. Review before seeding.")
        };
    }

    private sealed record VariantSeed(Guid Id, string Name, PriceSeed[] Prices);
    private sealed record PriceSeed(Guid Id, CustomerType CustomerType, decimal Amount);
}
