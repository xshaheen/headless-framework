// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

[UsedImplicitly]
public sealed class PulsarFixture : HeadlessPulsarFixture
{
    public ValueTask<TransportConsumerConformanceSession> CreateQueueSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true
    )
    {
        return CreateSessionAsync(
            ConnectionString,
            IntentType.Queue,
            cancellationToken,
            destination,
            group,
            createReplacement
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string group,
        CancellationToken cancellationToken,
        string? destination = null
    )
    {
        return CreateSessionAsync(ConnectionString, IntentType.Bus, cancellationToken, destination, group);
    }

    internal static async ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        string connectionString,
        IntentType intent,
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true
    )
    {
        destination ??= $"persistent://public/default/conf-{Guid.NewGuid():N}";
        group ??= $"group-{Guid.NewGuid():N}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHeadlessMessaging(setup =>
            setup.UsePulsar(options =>
            {
                options.ServiceUrl = connectionString;
                options.NegativeAckRedeliveryDelay = TimeSpan.FromSeconds(1);
            })
        );
        var serviceProvider = services.BuildServiceProvider();

        try
        {
            var producer = serviceProvider.GetRequiredService<IQueueTransport>();
            var factory = serviceProvider.GetRequiredService<IConsumerClientFactory>();
            var consumer = await ((IIntentAwareConsumerClientFactory)factory).CreateAsync(
                group,
                2,
                intent,
                cancellationToken
            );
            consumer.OnLogCallback = _ => { };

            try
            {
                await consumer.SubscribeAsync([destination], cancellationToken);

                return new TransportConsumerConformanceSession(
                    destination,
                    producer,
                    consumer,
                    TimeSpan.FromSeconds(3),
                    serviceProvider.DisposeAsync,
                    createReplacementSession: createReplacement
                        ? replacementToken =>
                            CreateSessionAsync(
                                connectionString,
                                intent,
                                replacementToken,
                                destination,
                                group,
                                createReplacement: false
                            )
                        : null
                );
            }
            catch
            {
                await consumer.DisposeAsync();
                throw;
            }
        }
        catch
        {
            await serviceProvider.DisposeAsync();
            throw;
        }
    }
}

[CollectionDefinition("Pulsar", DisableParallelization = true)]
public sealed class PulsarCollection : ICollectionFixture<PulsarFixture>;
