// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Caching;
using Headless.Idempotency;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackExchange.Redis;

namespace Tests;

/// <summary>
/// One Redis container shared by the collection. Each host a test builds opens its own connection to it, the way two
/// replicas of an application would, so a race between hosts crosses the network rather than one multiplexer.
/// </summary>
[CollectionDefinition(DisableParallelization = false)]
public sealed class RedisIdempotencyFixture : HeadlessRedisFixture, ICollectionFixture<RedisIdempotencyFixture>
{
    /// <summary>
    /// Builds a host with idempotency on the cache provider over its own Redis connection, with records under
    /// <paramref name="keyPrefix" /> so tests never share a record or a generation counter.
    /// </summary>
    public async Task<RedisIdempotencyHost> CreateHostAsync(string keyPrefix, CancellationToken cancellationToken)
    {
        var connection = await ConnectionMultiplexer.ConnectAsync(Container.GetConnectionString());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddHeadlessCaching(setup => setup.UseRedis(options => options.ConnectionMultiplexer = connection));
        services.AddHeadlessIdempotency(setup =>
        {
            setup.UseCache(options => options.KeyPrefix = keyPrefix);
            setup.ConfigureOptions(static options => options.PurgeInterval = null);
        });

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        foreach (var initializer in provider.GetServices<IHostedService>().OfType<IHostedLifecycleService>())
        {
            await initializer.StartingAsync(cancellationToken);
        }

        return new RedisIdempotencyHost(provider, connection);
    }
}

/// <summary>One replica: its own service provider and its own Redis connection.</summary>
public sealed class RedisIdempotencyHost(ServiceProvider services, ConnectionMultiplexer connection) : IAsyncDisposable
{
    public IServiceProvider Services => services;

    public IIdempotentOperations Operations { get; } = services.GetRequiredService<IIdempotentOperations>();

    public ICache Cache { get; } = services.GetRequiredService<ICache>();

    public IDatabase Database => connection.GetDatabase();

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }
}
