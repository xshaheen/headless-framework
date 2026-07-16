// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.CommitCoordination;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// EF Core/SQLite leaf fixture for the coordinated-transaction conformance suite. Wraps the plain
/// <c>DbContext.ExecuteCoordinatedTransactionAsync</c> helper. The interceptor is wired EXPLICITLY via
/// <c>AddInterceptors</c> so this fixture isolates the helper contract from the Headless ORM wiring
/// (which has its own regression test in Headless.EntityFramework.Tests.Integration).
/// </summary>
[UsedImplicitly]
public sealed class EfCoordinatedTransactionFixture : ICoordinatedTransactionFixture, IAsyncLifetime
{
    private SqliteConnection _connection = null!;
    private ServiceProvider _root = null!;

    public async ValueTask InitializeAsync()
    {
        // Shared in-memory SQLite lives only while a connection is open; the fixture holds one.
        _connection = new SqliteConnection("DataSource=:memory:");
        await _connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddEntityFrameworkCommitCoordination();
        services.AddDbContext<ProbeDbContext>(
            (sp, options) => options.UseSqlite(_connection).AddInterceptors(sp.GetServices<IInterceptor>())
        );

        _root = services.BuildServiceProvider();

        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _root.DisposeAsync();
        await _connection.DisposeAsync();
    }

    public async Task RunCoordinatedAsync(
        Func<ICoordinatedTransactionContext, CancellationToken, Task> operation,
        CancellationToken cancellationToken
    )
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        await db.ExecuteCoordinatedTransactionAsync(
            (ctx, ct) => operation(new EfCoordinatedTransactionContext(scope.ServiceProvider, ctx), ct),
            scope.ServiceProvider,
            cancellationToken: cancellationToken
        );
    }

    public async Task<int> CountProbeRowsAsync(CancellationToken cancellationToken)
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        return await db.Probes.AsNoTracking().CountAsync(cancellationToken);
    }

    public async Task ResetAsync(CancellationToken cancellationToken)
    {
        await using var scope = _root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();
        await db.Probes.ExecuteDeleteAsync(cancellationToken);
    }

    private sealed class EfCoordinatedTransactionContext(IServiceProvider services, DbContext db)
        : ICoordinatedTransactionContext
    {
        // Lazy on every read: an execution-strategy retry opens a fresh coordinator.
        public ICommitCoordinator Coordinator =>
            services.GetRequiredService<ICurrentCommitCoordinator>().Current
            ?? throw new InvalidOperationException("No ambient coordinator — the helper did not enlist.");

        public async Task InsertProbeRowAsync(string name, CancellationToken cancellationToken)
        {
            db.Set<ProbeRow>().Add(new ProbeRow { Name = name });
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    public sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
    {
        public DbSet<ProbeRow> Probes => Set<ProbeRow>();
    }

    public sealed class ProbeRow
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
