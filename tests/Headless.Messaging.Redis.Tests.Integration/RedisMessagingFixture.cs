// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Redis;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using StackExchange.Redis;

namespace Tests;

[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class RedisMessagingFixture : HeadlessRedisFixture, ICollectionFixture<RedisMessagingFixture>
{
    private ConnectionMultiplexer? _admin;

    public string ConnectionString => Container.GetConnectionString();

    /// <summary>Counts the clients subscribed to <paramref name="channel"/> as a literal channel (<c>PUBSUB NUMSUB</c>).</summary>
    public async Task<long> CountSubscribersAsync(string channel, CancellationToken cancellationToken)
    {
        var admin = await _GetAdminAsync();

        return await _Server(admin)
            .SubscriptionSubscriberCountAsync(RedisChannel.Literal(channel))
            .WaitAsync(cancellationToken);
    }

    /// <summary>Lists every key on the server matching the glob <paramref name="pattern"/>, by <c>SCAN</c>.</summary>
    public async Task<IReadOnlyList<string>> ScanKeysAsync(string pattern, CancellationToken cancellationToken)
    {
        var admin = await _GetAdminAsync();
        var keys = new List<string>();

        await foreach (var key in _Server(admin).KeysAsync(pattern: pattern).WithCancellation(cancellationToken))
        {
            keys.Add(key.ToString());
        }

        return keys;
    }

    /// <summary>Closes every pub/sub client connection on the server (<c>CLIENT KILL TYPE pubsub</c>).</summary>
    public async Task KillPubSubClientsAsync(CancellationToken cancellationToken)
    {
        var admin = await _GetAdminAsync();

        await _Server(admin).ClientKillAsync(clientType: ClientType.PubSub).WaitAsync(cancellationToken);
    }

    /// <summary>A connection for the test itself, apart from every pool under test.</summary>
    public async Task<IConnectionMultiplexer> GetAdminConnectionAsync()
    {
        return await _GetAdminAsync();
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        if (_admin is not null)
        {
            await _admin.DisposeAsync();
        }

        await base.DisposeAsyncCore();
    }

    private async Task<ConnectionMultiplexer> _GetAdminAsync()
    {
        if (_admin is not null)
        {
            return _admin;
        }

        var options = ConfigurationOptions.Parse(ConnectionString);
        // SCAN and CLIENT KILL are admin commands.
        options.AllowAdmin = true;

        return _admin = await ConnectionMultiplexer.ConnectAsync(options);
    }

    private static IServer _Server(ConnectionMultiplexer admin)
    {
        return admin.GetServer(admin.GetEndPoints()[0]);
    }

    public async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        MessageLane lane,
        string destination,
        string group,
        CancellationToken cancellationToken,
        bool ownsStream = true,
        Func<RedisMessagingOptions.ConsumeErrorContext, Task>? onConsumeError = null,
        ConsumerClientRequest? request = null,
        Action<RedisMessagingOptions>? configure = null,
        TimeSpan? listeningTimeout = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseRedis(options =>
            {
                options.Configuration = ConfigurationOptions.Parse(ConnectionString);
                options.OnConsumeError = onConsumeError;

                // A rejected entry stays pending until the claim pass takes it, so the conformance suite's reject
                // redelivery has to fit inside its receive timeout.
                options.PendingClaimMinIdleTime = TimeSpan.FromSeconds(1);
                configure?.Invoke(options);
            })
        );
        var provider = services.BuildServiceProvider();

        try
        {
            var producer =
                lane == MessageLane.Bus
                    ? (ITransport)provider.GetRequiredService<IBusTransport>()
                    : provider.GetRequiredService<IQueueTransport>();
            var factory = provider.GetRequiredService<IConsumerClientFactory>();
            var consumer = await factory.CreateAsync(
                request ?? new ConsumerClientRequest(group, 1, lane),
                cancellationToken
            );
            await consumer.SubscribeAsync([destination], cancellationToken);
            var physicalStream = RedisPhysicalAddress.ForLane(lane, destination);

            return new TransportConsumerConformanceSession(
                destination,
                producer,
                consumer,
                TimeSpan.FromSeconds(2),
                async () =>
                {
                    try
                    {
                        await provider.DisposeAsync();
                    }
                    finally
                    {
                        if (ownsStream)
                        {
                            await using var connection = await ConnectionMultiplexer.ConnectAsync(ConnectionString);
                            await connection.GetDatabase().KeyDeleteAsync(physicalStream);
                        }
                    }
                },
                listeningTimeout: listeningTimeout,
                createReplacementSession: replacementToken =>
                    CreateSessionAsync(
                        lane,
                        destination,
                        group,
                        replacementToken,
                        ownsStream: false,
                        onConsumeError,
                        request,
                        configure,
                        listeningTimeout
                    )
            );
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }
}
