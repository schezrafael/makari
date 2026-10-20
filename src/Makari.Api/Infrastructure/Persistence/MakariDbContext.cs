using Microsoft.EntityFrameworkCore;

namespace Makari.Api.Infrastructure.Persistence;

public sealed class MakariDbContext(DbContextOptions<MakariDbContext> options)
    : DbContext(options);
