// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.UnitOfWork;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// One relational provider under the EF Core conflict-replay conformance suite. <paramref name="useProvider" />
/// points every context at the provider's database, and <paramref name="resetSql" /> creates the
/// <c>conflict_counters</c> table when it is absent and empties it, in the provider's own DDL.
/// </summary>
public sealed class EntityFrameworkConflictReplayFixture(Action<DbContextOptionsBuilder> useProvider, string resetSql)
{
    /// <summary>
    /// Builds a host whose scopes each own one <see cref="ConflictCounterDbContext" /> on its own connection, so
    /// concurrent units hold concurrent transactions on the server.
    /// </summary>
    public ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();
        services.AddDbContext<ConflictCounterDbContext>(useProvider);

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    /// <summary>Resets the table and stores one counter row at <paramref name="counter" />, returning its id.</summary>
    public async Task<int> SeedCounterAsync(int counter, CancellationToken cancellationToken)
    {
        await using var db = _CreateContext();
        await db.Database.ExecuteSqlRawAsync(resetSql, cancellationToken);
        var row = new ConflictCounterRow { Counter = counter };
        db.Counters.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return row.Id;
    }

    /// <summary>Reads the stored counter outside any unit's transaction.</summary>
    public async Task<int> ReadCounterAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = _CreateContext();

        return await db
            .Counters.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.Counter)
            .SingleAsync(cancellationToken);
    }

    /// <summary>Reads the stored version token outside any unit's transaction.</summary>
    public async Task<int> ReadVersionAsync(int id, CancellationToken cancellationToken)
    {
        await using var db = _CreateContext();

        return await db
            .Counters.AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => x.Version)
            .SingleAsync(cancellationToken);
    }

    private ConflictCounterDbContext _CreateContext()
    {
        var builder = new DbContextOptionsBuilder<ConflictCounterDbContext>();
        useProvider(builder);

        return new ConflictCounterDbContext(builder.Options);
    }
}

/// <summary>Maps <c>conflict_counters</c>, whose <c>version</c> column is the optimistic concurrency token.</summary>
public sealed class ConflictCounterDbContext(DbContextOptions<ConflictCounterDbContext> options) : DbContext(options)
{
    public DbSet<ConflictCounterRow> Counters => Set<ConflictCounterRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ConflictCounterRow>(entity =>
        {
            entity.ToTable("conflict_counters");
            entity.HasKey(row => row.Id);
            entity.Property(row => row.Id).HasColumnName("id");
            entity.Property(row => row.Counter).HasColumnName("counter");
            entity.Property(row => row.Version).HasColumnName("version").IsConcurrencyToken();
        });
    }
}

public sealed class ConflictCounterRow
{
    public int Id { get; set; }

    public int Counter { get; set; }

    public int Version { get; set; }
}
