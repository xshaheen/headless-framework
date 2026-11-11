// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.LiveQueries;
using Headless.Testing.Tests;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public sealed class LiveStreamLifecycleTests : TestBase
{
    // Ends a wait that would otherwise hang the run; no assertion depends on it.
    private static readonly TimeSpan _Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task should_close_the_subscription_when_the_client_leaves()
    {
        // given
        await using var app = await LiveTestApp.StartAsync(_MapTodos, AbortToken);
        using var stream = await app.OpenAsync("/todos", AbortToken);
        await stream.NextResultAsync(AbortToken);
        var openWhileConnected = app.Meters.Total("headless.live_queries.subscriptions.active");

        // when
        stream.Dispose();

        // then
        openWhileConnected.Should().Be(1);
        await app.Meters.ReachedAsync("headless.live_queries.subscriptions.active", 0).WaitAsync(_Patience, AbortToken);
    }

    [Fact]
    public async Task should_close_the_subscription_when_the_client_leaves_while_a_rerun_waits_its_interval_out()
    {
        // given - a stream stopped between two runs by the minimum interval
        var clock = new RerunClock();
        await using var app = await LiveTestApp.StartAsync(
            _MapTodos,
            AbortToken,
            configureLive: options => options.MinRerunInterval = TimeSpan.FromMinutes(1),
            configureBuilder: builder =>
                builder.Services.AddKeyedSingleton<TimeProvider>(LiveEndpointsOptions.RerunClockServiceKey, clock)
        );
        using var stream = await app.OpenAsync("/todos", AbortToken);
        await stream.NextResultAsync(AbortToken);
        await app.InvalidateAsync("todos", AbortToken);
        await clock.Waiting.WaitAsync(_Patience, AbortToken);

        // when
        stream.Dispose();

        // then
        await app.Meters.ReachedAsync("headless.live_queries.subscriptions.active", 0).WaitAsync(_Patience, AbortToken);
    }

    [Fact]
    public async Task should_end_every_open_stream_when_the_host_stops()
    {
        // given - one open stream of each built-in format
        await using var app = await LiveTestApp.StartAsync(_MapTodos, AbortToken);
        using var events = await app.OpenAsync("/todos", AbortToken);
        using var lines = await app.OpenAsync("/todos", AbortToken, accept: "application/x-ndjson");
        await events.NextResultAsync(AbortToken);
        await lines.NextLineAsync(AbortToken);

        // when
        await app.App.StopAsync(AbortToken);

        // then - both responses end
        (await events.NextEventAsync(AbortToken))
            .Should()
            .BeNull();
        (await lines.NextLineAsync(AbortToken)).Should().BeNull();
        app.Meters.Total("headless.live_queries.subscriptions.active").Should().Be(0);
    }

    private static void _MapTodos(WebApplication application)
    {
        application
            .MapGet(
                "/todos",
                (ILiveQueryDependencies dependencies) =>
                {
                    dependencies.DependsOn("todos");

                    return new[] { "milk" };
                }
            )
            .WithLiveUpdates();
    }
}
