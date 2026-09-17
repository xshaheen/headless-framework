// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Headless.Testing.Tests;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Tests;

/// <summary>
/// Shared SQLite in-memory host for the EF provider scenarios: one open connection (in-memory SQLite lives
/// only while a connection is open), <c>AddUnitOfWork</c> + <c>AddEntityFrameworkUnitOfWork</c>, and a
/// capturing logger provider that records the unit-of-work manager's entries. Each session is one service
/// scope with its own scoped manager.
/// </summary>
internal sealed class EfUnitOfWorkHost(
    SqliteConnection connection,
    ServiceProvider root,
    CapturingLoggerProvider loggerProvider
) : IAsyncDisposable
{
    public SqliteConnection Connection { get; } = connection;

    public ServiceProvider Root { get; } = root;

    private CapturingLoggerProvider LoggerProvider { get; } = loggerProvider;

    [SuppressMessage(
        "Usage",
        "CA2000:Dispose objects before losing scope",
        Justification = "The host owns the connection and logger provider it was built with and disposes both in DisposeAsync."
    )]
    public static async Task<EfUnitOfWorkHost> CreateAsync(
        Action<IServiceCollection>? configureServices = null,
        Action<DbContextOptionsBuilder>? configureOptions = null
    )
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var loggerProvider = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddProvider(loggerProvider));
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();

        // A plain AddDbContext does NOT auto-attach DI-registered IInterceptor instances (the same
        // footgun the old commit-coordination interceptor documented) — attach them explicitly so the
        // commit-fault scenarios' interceptor actually fires.
        services.AddDbContext<ProbeDbContext>(
            (sp, options) =>
            {
                options.UseSqlite(connection).AddInterceptors(sp.GetServices<IInterceptor>());
                configureOptions?.Invoke(options);
            }
        );
        configureServices?.Invoke(services);

        var root = services.BuildServiceProvider();

        await using var scope = root.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<ProbeDbContext>().Database.EnsureCreatedAsync();

        return new EfUnitOfWorkHost(connection, root, loggerProvider);
    }

    public EfUnitOfWorkSession CreateSession()
    {
        return new EfUnitOfWorkSession(Root.CreateAsyncScope(), LoggerProvider);
    }

    /// <summary>Counts durable probe rows through a fresh scope and change tracker.</summary>
    public async Task<int> CountProbeRowsAsync()
    {
        await using var scope = Root.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

        return await db.Probes.AsNoTracking().CountAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Root.DisposeAsync();
        await Connection.DisposeAsync();
    }
}

/// <summary>
/// One isolated service scope: the scope's scoped <see cref="IUnitOfWorkManager" /> with its captured log
/// entries, and a <see cref="ProbeDbContext" /> sharing the host's connection. Disposing the session ends
/// the scope (the service-scope edge). Log entries come from the host's capturing provider, so the internal
/// manager type is never referenced.
/// </summary>
internal sealed class EfUnitOfWorkSession(AsyncServiceScope scope, CapturingLoggerProvider provider)
{
    public IUnitOfWorkManager Manager { get; } = scope.ServiceProvider.GetRequiredService<IUnitOfWorkManager>();

    public ProbeDbContext Db { get; } = scope.ServiceProvider.GetRequiredService<ProbeDbContext>();

    /// <summary>
    /// The unit-of-work manager's entries only: EF's info-level SQL chatter flows through the same provider
    /// and must not pollute the assertions on the forgotten-completion warning.
    /// </summary>
    public IReadOnlyCollection<LogEntry> Logs =>
        provider
            .Entries.Where(e => e.Category.StartsWith("Headless.UnitOfWork", StringComparison.Ordinal))
            .Select(e => new LogEntry(e.Level, e.EventId, e.Message))
            .ToArray();

    public async ValueTask DisposeAsync()
    {
        await scope.DisposeAsync();
    }
}

internal sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
{
    public DbSet<ProbeRow> Probes => Set<ProbeRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeRow>().HasKey(x => x.Id);
    }
}

internal sealed class ProbeRow
{
    public int Id { get; set; }

    public required string Name { get; init; }
}

/// <summary>A transient marker exception the retrying strategy below retries once.</summary>
internal sealed class TransientMarkerException() : Exception("Simulated transient failure.");

/// <summary>An execution strategy that retries <see cref="TransientMarkerException" /> once with no delay.</summary>
internal sealed class RetryExecutionStrategy(ExecutionStrategyDependencies dependencies)
    : ExecutionStrategy(dependencies, maxRetryCount: 1, maxRetryDelay: TimeSpan.Zero)
{
    protected override bool ShouldRetryOn(Exception exception)
    {
        return exception is TransientMarkerException;
    }
}

internal sealed class RetryExecutionStrategyFactory(ExecutionStrategyDependencies dependencies)
    : IExecutionStrategyFactory
{
    public IExecutionStrategy Create()
    {
        return new RetryExecutionStrategy(dependencies);
    }
}
