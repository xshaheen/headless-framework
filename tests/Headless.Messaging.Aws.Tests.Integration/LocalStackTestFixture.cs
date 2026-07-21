// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Messaging.Transport;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Tests;

/// <summary>
/// Collection fixture providing a LocalStack container for AWS SQS/SNS integration tests.
/// </summary>
[UsedImplicitly]
[CollectionDefinition(DisableParallelization = true)]
public sealed class LocalStackTestFixture : HeadlessLocalStackFixture, ICollectionFixture<LocalStackTestFixture>
{
    /// <summary>Gets the LocalStack connection string (service URL).</summary>
    public string ConnectionString => Container.GetConnectionString();

    public ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool ownsQueue = true
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            ownsQueue,
            createReplacement: ownsQueue,
            cancellationToken
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string destination,
        string group,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Bus,
            destination,
            group,
            ownsQueue: true,
            createReplacement: false,
            cancellationToken
        );
    }

    private async ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        MessageLane lane,
        string? destination,
        string? group,
        bool ownsQueue,
        bool createReplacement,
        CancellationToken cancellationToken
    )
    {
        destination ??= $"conf-{Guid.NewGuid():N}";
        group ??= $"group-{Guid.NewGuid():N}";
        var options = Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = ConnectionString,
                SqsServiceUrl = ConnectionString,
                Credentials = new BasicAWSCredentials("test", "test"),
            }
        );

#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        ITransport producer = lane switch
        {
            MessageLane.Bus => new AmazonSnsBusTransport(NullLogger<AmazonSnsBusTransport>.Instance, options),
            MessageLane.Queue => new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, options),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
        };
        var consumer = new AmazonSqsConsumerClient(
            group,
            1,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            lane switch
            {
                MessageLane.Bus => IntentType.Bus,
                MessageLane.Queue => IntentType.Queue,
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
            }
        );
        var cleanupClient = AwsClientFactory.CreateSqsClient(options.Value);
#pragma warning restore CA2000

        try
        {
            var brokerDestinations = await consumer.FetchMessageNamesAsync([destination], cancellationToken);
            await consumer.SubscribeAsync(brokerDestinations, cancellationToken);
            var queueUrl =
                lane == MessageLane.Queue
                    ? brokerDestinations.Single()
                    : (
                        await cleanupClient.GetQueueUrlAsync(group.NormalizeForSqsQueueName(), cancellationToken)
                    ).QueueUrl;
            if (ownsQueue)
            {
                await cleanupClient.SetQueueAttributesAsync(
                    queueUrl,
                    new Dictionary<string, string>(StringComparer.Ordinal) { ["VisibilityTimeout"] = "2" },
                    cancellationToken
                );
            }

            return new TransportConsumerConformanceSession(
                destination,
                producer,
                consumer,
                TimeSpan.FromSeconds(5),
                async () =>
                {
                    try
                    {
                        if (ownsQueue)
                        {
                            await cleanupClient.DeleteQueueAsync(queueUrl, CancellationToken.None);
                        }
                    }
                    finally
                    {
                        cleanupClient.Dispose();
                    }
                },
                createReplacementSession: createReplacement
                    ? replacementToken =>
                        _CreateConformanceSessionAsync(
                            lane,
                            destination,
                            group,
                            ownsQueue: false,
                            createReplacement: false,
                            replacementToken
                        )
                    : null
            );
        }
        catch
        {
            await consumer.DisposeAsync();
            await producer.DisposeAsync();
            cleanupClient.Dispose();
            throw;
        }
    }
}
