// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Globalization;
using Headless.DistributedLocks;
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
public sealed class RedisDistributedLockScriptLoadingTests : TestBase
{
    private static readonly DistributedLockAcquireOptions _NoWait = new() { AcquireTimeout = TimeSpan.Zero };

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
        // given
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddSingleton<IConnectionMultiplexer>(_multiplexer);
        builder.Services.AddHeadlessDistributedLocks(setup => setup.UseRedis());

        using var host = builder.Build();

        // when
        await host.StartAsync(AbortToken);

        // then - the host start made no SCRIPT LOAD
        (await _CachedScriptCountAsync())
            .Should()
            .Be(0);

        // when - the first acquire of each lock family
        var mutex = await host
            .Services.GetRequiredService<IDistributedLock>()
            .TryAcquireAsync("script-loading-mutex", _NoWait, AbortToken);
        var write = await host
            .Services.GetRequiredService<IDistributedReadWriteLock>()
            .TryAcquireWriteLockAsync("script-loading-rw", _NoWait, AbortToken);
        var permit = await host
            .Services.GetRequiredService<IDistributedSemaphoreProvider>()
            .CreateSemaphore("script-loading-semaphore", maxCount: 1)
            .TryAcquireAsync(_NoWait, AbortToken);

        // then - each acquire loaded its script on demand and succeeded
        mutex.Should().NotBeNull();
        write.Should().NotBeNull();
        permit.Should().NotBeNull();
        (await _CachedScriptCountAsync()).Should().BePositive();

        await mutex!.DisposeAsync();
        await write!.DisposeAsync();
        await permit!.DisposeAsync();
        await host.StopAsync(AbortToken);
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
