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
    public string ConnectionString => Container.GetConnectionString();

    public async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        MessageLane lane,
        string destination,
        string group,
        CancellationToken cancellationToken,
        bool ownsStream = true,
        Func<RedisMessagingOptions.ConsumeErrorContext, Task>? onConsumeError = null
    )
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UseRedis(options =>
            {
                options.Configuration = ConfigurationOptions.Parse(ConnectionString);
                options.OnConsumeError = onConsumeError;
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
            var consumer = await factory.CreateAsync(group, 1, lane, cancellationToken);
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
                createReplacementSession: replacementToken =>
                    CreateSessionAsync(lane, destination, group, replacementToken, ownsStream: false, onConsumeError)
            );
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }
}
