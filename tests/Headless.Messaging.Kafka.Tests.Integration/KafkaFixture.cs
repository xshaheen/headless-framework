// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Headless.Messaging.Kafka;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

[UsedImplicitly]
public sealed class KafkaFixture : HeadlessKafkaFixture
{
    public async ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool createReplacement = true,
        Action<KafkaMessagingOptions>? configure = null
    )
    {
        destination ??= $"conf-{Guid.NewGuid():N}";
        group ??= $"group-{Guid.NewGuid():N}";

        var services = new ServiceCollection().BuildServiceProvider();
        var options = Options.Create(
            new KafkaMessagingOptions
            {
                Servers = ConnectionString,
                TopicOptions = { NumPartitions = 1, ReplicationFactor = 1 },
            }
        );
        options.Value.MainConfig["allow.auto.create.topics"] = "true";
        configure?.Invoke(options.Value);

#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        var producers = new KafkaProducerProvider(NullLogger<KafkaProducerProvider>.Instance, options);
        var producer = new KafkaTransport(NullLogger<KafkaTransport>.Instance, producers);
        var consumer = new KafkaConsumerClient(group, 2, options, services);
#pragma warning restore CA2000

        try
        {
            var topics = await consumer.FetchMessageNamesAsync([destination], cancellationToken);
            await consumer.SubscribeAsync(topics, cancellationToken);

            return new TransportConsumerConformanceSession(
                destination,
                producer,
                consumer,
                TimeSpan.FromSeconds(3),
                async () =>
                {
                    producers.Dispose();
                    await services.DisposeAsync();
                },
                listeningTimeout: TimeSpan.FromSeconds(1),
                createReplacementSession: createReplacement
                    ? replacementToken =>
                        CreateConformanceSessionAsync(
                            replacementToken,
                            destination,
                            group,
                            createReplacement: false,
                            configure
                        )
                    : null
            );
        }
        catch
        {
            await consumer.DisposeAsync();
            producers.Dispose();
            await services.DisposeAsync();
            throw;
        }
    }
}

[CollectionDefinition("Kafka", DisableParallelization = true)]
public sealed class KafkaCollection : ICollectionFixture<KafkaFixture>;
