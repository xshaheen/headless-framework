// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Diagnostics.Metrics;
using Headless.LiveQueries;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

/// <summary>A service provider with live queries registered and its own meter factory.</summary>
internal sealed class LiveQueriesTestHost : IAsyncDisposable
{
    private readonly ServiceProvider _provider;

    public LiveQueriesTestHost(Action<LiveQueriesOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IMeterFactory>(Meters);
        services.AddHeadlessLiveQueries(setup =>
        {
            if (configure is not null)
            {
                setup.ConfigureOptions(configure);
            }
        });
        _provider = services.BuildServiceProvider();
    }

    public TestMeterFactory Meters { get; } = new();

    public IServiceProvider Services => _provider;

    public ILiveQueries Queries => _provider.GetRequiredService<ILiveQueries>();

    public ILiveQueryInvalidator Invalidator => _provider.GetRequiredService<ILiveQueryInvalidator>();

    public long ActiveSubscriptions => Meters.Total("headless.live_queries.subscriptions.active");

    public async ValueTask DisposeAsync()
    {
        await _provider.DisposeAsync();
        Meters.Dispose();
    }
}
