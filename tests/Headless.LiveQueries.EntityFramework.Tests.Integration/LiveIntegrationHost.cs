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

    public LiveIntegrationHost(ILiveDatabase database)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IMeterFactory>(Meters);
        services.AddUnitOfWork();
        services.AddEntityFrameworkUnitOfWork();
        services.AddHeadlessLiveQueries(static setup => setup.UseEntityFramework<LiveOrdersDb>());
        services.AddDbContext<LiveOrdersDb>(database.Configure);
        _provider = services.BuildServiceProvider(validateScopes: true);
    }

    public TestMeterFactory Meters { get; } = new();

    public IUnitOfWorkFactory UnitOfWork => _provider.GetRequiredService<IUnitOfWorkFactory>();

    public ILiveQueryInvalidator Invalidator => _provider.GetRequiredService<ILiveQueryInvalidator>();

    /// <summary>How many times an announcement has signalled a live query of this host.</summary>
    public long Wakes => Meters.Total("headless.live_queries.wakes");

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

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Meters.Dispose();
    }
}
