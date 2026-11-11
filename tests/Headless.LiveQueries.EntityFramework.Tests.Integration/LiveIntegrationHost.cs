// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.LiveQueries;
using Headless.UnitOfWork;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A host with live queries tracking <see cref="LiveOrdersDb" /> on one provider's database.</summary>
internal sealed class LiveIntegrationHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    /// <summary>Creates the host.</summary>
    /// <param name="database">The provider's database.</param>
    /// <param name="rowLevel">Whether saved rows are matched against each live query's filters.</param>
    public LiveIntegrationHost(ILiveDatabase database, bool rowLevel = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMeterFactory>(Meters);
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();
        services.AddHeadlessLiveQueries(setup =>
            setup.UseEntityFramework<LiveOrdersDb>(options => options.RowLevelMatching = rowLevel)
        );
        services.AddDbContext<LiveOrdersDb>(database.Configure);
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public TestMeterFactory Meters { get; } = new();

    public IUnitOfWorkFactory UnitOfWork => _provider.GetRequiredService<IUnitOfWorkFactory>();

    public ILiveQueryInvalidator Invalidator => _provider.GetRequiredService<ILiveQueryInvalidator>();

    /// <summary>How many times an announcement has signalled a live query of this host.</summary>
    public long Wakes => Meters.Total("headless.live_queries.wakes");

    /// <summary>How many saved rows a live query's row filters showed could not change its result.</summary>
    public long RowsRuledOut => Meters.Total("headless.live_queries.rows.ruled_out");

    /// <summary>How many times tracking failed and fell back to announcing whole tables.</summary>
    public long Fallbacks => Meters.Total("headless.live_queries.tracking.fallbacks");

    public AsyncServiceScope CreateScope() => _provider.CreateAsyncScope();

    /// <summary>Watches a query over <see cref="LiveOrdersDb" />; nothing runs until its first <c>NextAsync</c>.</summary>
    public Watch<T> Watch<T>(Func<LiveOrdersDb, CancellationToken, Task<T>> query, CancellationToken abortToken)
    {
        return new Watch<T>(
            _provider.GetRequiredService<ILiveQueries>(),
            async (services, cancellationToken) =>
                await query(services.GetRequiredService<LiveOrdersDb>(), cancellationToken),
            abortToken
        );
    }

    /// <summary>Runs <paramref name="work" /> on a context of its own scope and connection, as one request does.</summary>
    public async Task WriteAsync(Func<LiveOrdersDb, Task> work)
    {
        await using var scope = CreateScope();
        await work(scope.ServiceProvider.GetRequiredService<LiveOrdersDb>());
    }

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Meters.Dispose();
    }
}
