// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.Context;
using Headless.Coordination;
using Headless.Hosting;
using Headless.Testing.Testcontainers;
using Headless.Testing.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;
using Testcontainers.Redis;

namespace Tests;

/// <summary>
/// Runs against its own Redis container, so the server script cache starts empty and no other test
/// can load a script between the host start and the assertions.
/// </summary>
public sealed class RedisMembershipScriptLoadingTests : TestBase
{
    private readonly RedisContainer _container = new RedisBuilder(TestImages.Redis).Build();
    private ConnectionMultiplexer _multiplexer = null!;

    public override async ValueTask InitializeAsync()
    {
        await base.InitializeAsync();
        await _container.StartAsync(AbortToken);
        _multiplexer = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString() + ",allowAdmin=true");
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_multiplexer is not null)
        {
            await _multiplexer.DisposeAsync();
        }

        await _container.DisposeAsync();
        await base.DisposeAsyncCore();
    }

    [Fact]
    public async Task should_start_without_loading_scripts_and_load_them_on_first_use_when_use_redis()
    {
        // given - the heartbeat is off so nothing touches Redis until the test registers a node
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_multiplexer);
        services.AddHeadlessHostIdentity(options => options.HostName = "node-a");
        services.AddHeadlessCoordination(setup =>
        {
            setup.UseRedis(static _ => { });
            setup.DisableMembershipHeartbeat();
            setup.Configure(options => options.ClusterName = "script-loading");
        });

        await using var provider = services.BuildServiceProvider();
        var hostedServices = provider.GetServices<IHostedService>().ToList();

        // when - the same start sequence a host runs
        foreach (var hostedService in hostedServices.OfType<IHostedLifecycleService>())
        {
            await hostedService.StartingAsync(AbortToken);
        }

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StartAsync(AbortToken);
        }

        // then - the host start made no SCRIPT LOAD
        (await _CachedScriptCountAsync())
            .Should()
            .Be(0);

        // when - the first membership operations
        var membership = provider.GetRequiredService<INodeMembership>();
        var identity = await membership.RegisterAsync(AbortToken);
        var snapshot = await membership.GetLivenessSnapshotAsync(AbortToken);

        // then - each operation loaded its script on demand and succeeded
        snapshot.Should().ContainSingle(entry => entry.Identity == identity && entry.State == NodeLivenessState.Alive);
        (await _CachedScriptCountAsync()).Should().BePositive();

        foreach (var hostedService in hostedServices)
        {
            await hostedService.StopAsync(AbortToken);
        }
    }

    [Fact]
    public async Task should_load_only_this_packages_scripts_in_a_warmup_that_does_not_gate_readiness()
    {
        // given
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConnectionMultiplexer>(_multiplexer);
        services.AddHeadlessHostIdentity(options => options.HostName = "node-a");
        services.AddHeadlessCoordination(setup =>
        {
            setup.UseRedis(static _ => { });
            setup.DisableMembershipHeartbeat();
            setup.Configure(options => options.ClusterName = "script-loading");
        });

        await using var provider = services.BuildServiceProvider();
        var warmup = provider
            .GetServices<IWarmup>()
            .Should()
            .ContainSingle(w => w.Name == "RedisCoordinationScripts")
            .Which;

        // when
        await warmup.WarmUpAsync(AbortToken);

        // then - the membership store's scripts
        warmup.GatesReadiness.Should().BeFalse();
        (await _CachedScriptCountAsync()).Should().Be(7);
    }

    private async Task<long> _CachedScriptCountAsync()
    {
        var server = _multiplexer.GetServer(_multiplexer.GetEndPoints().Single());
        var info = await server.InfoAsync("memory");
        var count = info.SelectMany(group => group)
            .Single(pair => string.Equals(pair.Key, "number_of_cached_scripts", StringComparison.Ordinal))
            .Value;

        return long.Parse(count, CultureInfo.InvariantCulture);
    }
}
