// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using Headless.LiveQueries;
using Headless.UnitOfWork;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>
/// A host with live queries tracking <see cref="LiveDb" /> and <see cref="NotesDb" /> on SQLite in memory. Every
/// context shares the host's one open connection, because an in-memory SQLite database lives only on its connection.
/// </summary>
internal sealed class LiveEfTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    private LiveEfTestHost(SqliteConnection connection, ServiceProvider provider, TestMeterFactory meters)
    {
        Connection = connection;
        _provider = provider;
        Meters = meters;
    }

    public SqliteConnection Connection { get; }

    public TestMeterFactory Meters { get; }

    public IServiceProvider Services => _provider;

    public IUnitOfWorkFactory UnitOfWork => _provider.GetRequiredService<IUnitOfWorkFactory>();

    /// <summary>How many times an announcement has signalled a live query of this host.</summary>
    public long Wakes => Meters.Total("headless.live_queries.wakes");

    /// <summary>How many times tracking failed and fell back to announcing whole tables.</summary>
    public long Fallbacks => Meters.Total("headless.live_queries.tracking.fallbacks");

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The host owns the connection and the meter factory and disposes both in DisposeAsync."
    )]
    public static async Task<LiveEfTestHost> CreateAsync(
        Action<DbContextOptionsBuilder>? configureOptions = null,
        Action<SqliteDbContextOptionsBuilder>? configureSqlite = null,
        Action<LiveQueriesEntityFrameworkOptions>? configureTracking = null,
        bool pooled = false,
        bool trackerRunsFirst = false
    )
    {
        var connection = new SqliteConnection("DataSource=:memory:");
        await connection.OpenAsync();
        var meters = new TestMeterFactory();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMeterFactory>(meters);
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();

        // Interceptors run in registration order. By default the tracker's come after a test's own, so a test can
        // make an earlier step of a save or commit throw; a test asks for the other order to throw after them.
        void AddLiveQueries()
        {
            services.AddHeadlessLiveQueries(setup =>
                setup
                    .UseEntityFramework<LiveDb>(options => configureTracking?.Invoke(options))
                    .UseEntityFramework<NotesDb>()
            );
        }

        if (trackerRunsFirst)
        {
            AddLiveQueries();
        }

        void Configure(DbContextOptionsBuilder options)
        {
            options.UseSqlite(connection, configureSqlite);
            configureOptions?.Invoke(options);
        }

        if (pooled)
        {
            services.AddDbContextPool<LiveDb>(Configure, poolSize: 1);
        }
        else
        {
            services.AddDbContext<LiveDb>(Configure);
        }

        services.AddDbContext<NotesDb>(options => options.UseSqlite(connection));

        if (!trackerRunsFirst)
        {
            AddLiveQueries();
        }

        var provider = services.BuildServiceProvider(validateScopes: true);

        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<LiveDb>().Database.EnsureCreatedAsync();

            // EnsureCreated sees a database that already has tables, so the second model's are created directly.
            var notes = scope.ServiceProvider.GetRequiredService<NotesDb>();
            await notes.GetService<IRelationalDatabaseCreator>().CreateTablesAsync();
        }

        return new LiveEfTestHost(connection, provider, meters);
    }

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    /// <summary>Watches a query over <see cref="LiveDb" />; nothing runs until its first <c>NextAsync</c>.</summary>
    public Watch<T> Watch<T>(Func<LiveDb, CancellationToken, Task<T>> query, CancellationToken abortToken)
    {
        return new Watch<T>(
            _provider.GetRequiredService<ILiveQueries>(),
            async (services, cancellationToken) =>
                await query(services.GetRequiredService<LiveDb>(), cancellationToken),
            abortToken
        );
    }

    /// <summary>Watches a query over <see cref="NotesDb" />.</summary>
    public Watch<T> WatchNotes<T>(Func<NotesDb, CancellationToken, Task<T>> query, CancellationToken abortToken)
    {
        return new Watch<T>(
            _provider.GetRequiredService<ILiveQueries>(),
            async (services, cancellationToken) =>
                await query(services.GetRequiredService<NotesDb>(), cancellationToken),
            abortToken
        );
    }

    public Watch<int> WatchTodoCount(CancellationToken abortToken)
    {
        return Watch((db, cancellationToken) => db.Todos.CountAsync(cancellationToken), abortToken);
    }

    public Watch<int> WatchCounterCount(CancellationToken abortToken)
    {
        return Watch((db, cancellationToken) => db.Counters.CountAsync(cancellationToken), abortToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        await Connection.DisposeAsync();
        Meters.Dispose();
    }
}
