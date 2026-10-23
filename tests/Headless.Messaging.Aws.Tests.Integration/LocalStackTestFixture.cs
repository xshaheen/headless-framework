// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.SQS.Model;
using Headless.Checks;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Testcontainers;
using Microsoft.Extensions.DependencyInjection;
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
    private static readonly IServiceProvider _NoServices = new ServiceCollection().BuildServiceProvider();

    /// <summary>Gets the LocalStack connection string (service URL).</summary>
    public string ConnectionString => Container.GetConnectionString();

    public ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool ownsQueue = true,
        TransportConformanceSessionOptions? options = null
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Queue,
            destination,
            group,
            ownsQueue,
            createReplacement: ownsQueue,
            cancellationToken,
            options
        );
    }

    public ValueTask<TransportConsumerConformanceSession> CreateBusSessionAsync(
        string destination,
        string group,
        CancellationToken cancellationToken,
        bool ownsQueue = true
    )
    {
        return _CreateConformanceSessionAsync(
            MessageLane.Bus,
            destination,
            group,
            ownsQueue,
            createReplacement: false,
            cancellationToken
        );
    }

    /// <summary>
    /// Opens a queue session on the LocalStack at <paramref name="serviceUrl"/>, for a test that runs its own LocalStack
    /// rather than the collection's.
    /// </summary>
    public static ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        string serviceUrl,
        CancellationToken cancellationToken
    )
    {
        return _CreateConformanceSessionAsync(
            serviceUrl,
            MessageLane.Queue,
            destination: null,
            group: null,
            ownsQueue: true,
            createReplacement: false,
            cancellationToken
        );
    }

    private ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        MessageLane lane,
        string? destination,
        string? group,
        bool ownsQueue,
        bool createReplacement,
        CancellationToken cancellationToken,
        TransportConformanceSessionOptions? options = null
    )
    {
        return _CreateConformanceSessionAsync(
            ConnectionString,
            lane,
            destination,
            group,
            ownsQueue,
            createReplacement,
            cancellationToken,
            options
        );
    }

    private static async ValueTask<TransportConsumerConformanceSession> _CreateConformanceSessionAsync(
        string serviceUrl,
        MessageLane lane,
        string? destination,
        string? group,
        bool ownsQueue,
        bool createReplacement,
        CancellationToken cancellationToken,
        TransportConformanceSessionOptions? sessionOptions = null
    )
    {
        sessionOptions ??= new TransportConformanceSessionOptions();

        // Only a FIFO queue orders by key, and SQS makes one of a name that ends in ".fifo".
        var suffix = sessionOptions.RoutingAffinity ? ".fifo" : string.Empty;
        destination ??= $"conf-{Guid.NewGuid():N}{suffix}";
        group ??= $"group-{Guid.NewGuid():N}{suffix}";
        var options = Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = serviceUrl,
                SqsServiceUrl = serviceUrl,
                Credentials = new BasicAWSCredentials("test", "test"),
                // Every receive sets the visibility, so it overrides the queue attribute below; both keep redelivery
                // conformance fast.
                VisibilityTimeout = TimeSpan.FromSeconds(2),
            }
        );

        if (sessionOptions.HeaderBuilder is { } headerBuilder)
        {
            options.Value.CustomHeadersBuilder = (message, _) =>
            {
                headerBuilder(SqsHeaderCodec.Decode(message.MessageAttributes).GetValueOrDefault(Headers.MessageId));
                return [];
            };
        }

#pragma warning disable CA2000 // Ownership transfers to the returned conformance session or the catch cleanup path.
        ITransport producer = lane switch
        {
            MessageLane.Bus => new AmazonSnsBusTransport(NullLogger<AmazonSnsBusTransport>.Instance, options),
            MessageLane.Queue => new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, options),
            _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
        };
        var consumer = new AmazonSqsConsumerClient(
            group,
            sessionOptions.Concurrency,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            lane switch
            {
                MessageLane.Bus => MessageLane.Bus,
                MessageLane.Queue => MessageLane.Queue,
                _ => throw new ArgumentOutOfRangeException(nameof(lane), lane, null),
            },
            // The custom headers builder receives the service provider, and the client refuses to call it without one.
            serviceProvider: _NoServices
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
                        await cleanupClient.GetQueueUrlAsync(
                            AwsPhysicalAddress.BusSubscriptionQueue(group),
                            cancellationToken
                        )
                    ).QueueUrl;
            string? deadLetterQueueUrl = null;
            if (ownsQueue)
            {
                var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["VisibilityTimeout"] = "2" };

                // SQS gives up on a message only through a redrive policy: past maxReceiveCount receives it moves the
                // message to the dead-letter queue.
                if (sessionOptions.DeliveryLimit is { } deliveryLimit)
                {
                    Ensure.False(sessionOptions.RoutingAffinity, "A FIFO queue's dead-letter queue must be FIFO too.");
                    deadLetterQueueUrl = (
                        await cleanupClient.CreateQueueAsync(_DeadLetterQueueName(destination), cancellationToken)
                    ).QueueUrl;
                    var deadLetterQueueArn = (
                        await cleanupClient.GetQueueAttributesAsync(deadLetterQueueUrl, ["QueueArn"], cancellationToken)
                    ).QueueARN;
                    attributes["RedrivePolicy"] = JsonSerializer.Serialize(
                        new { deadLetterTargetArn = deadLetterQueueArn, maxReceiveCount = deliveryLimit }
                    );
                }

                await cleanupClient.SetQueueAttributesAsync(queueUrl, attributes, cancellationToken);
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

                        if (deadLetterQueueUrl is not null)
                        {
                            await cleanupClient.DeleteQueueAsync(deadLetterQueueUrl, CancellationToken.None);
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
                            serviceUrl,
                            lane,
                            destination,
                            group,
                            ownsQueue: false,
                            createReplacement: false,
                            replacementToken,
                            sessionOptions
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

    /// <summary>
    /// Takes every message the dead-letter queue of <paramref name="session"/> holds, as an operator draining it would,
    /// and returns their message ids.
    /// </summary>
    public async ValueTask<IReadOnlyList<string>> TakeDeadLetteredMessageIdsAsync(
        TransportConsumerConformanceSession session,
        CancellationToken cancellationToken
    )
    {
        using var client = AwsClientFactory.CreateSqsClient(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = ConnectionString,
                SqsServiceUrl = ConnectionString,
                Credentials = new BasicAWSCredentials("test", "test"),
            }
        );
        var queueUrl = (
            await client.GetQueueUrlAsync(_DeadLetterQueueName(session.Destination), cancellationToken)
        ).QueueUrl;
        var taken = new List<string>();

        while (true)
        {
            var response = await client.ReceiveMessageAsync(
                new ReceiveMessageRequest(queueUrl)
                {
                    MaxNumberOfMessages = 10,
                    MessageAttributeNames = ["All"],
                    WaitTimeSeconds = 0,
                },
                cancellationToken
            );
            if (response.Messages is not { Count: > 0 } messages)
            {
                return taken;
            }

            foreach (var message in messages)
            {
                taken.Add(
                    SqsHeaderCodec.Decode(message.MessageAttributes).GetValueOrDefault(Headers.MessageId)
                        ?? string.Empty
                );
                await client.DeleteMessageAsync(queueUrl, message.ReceiptHandle, cancellationToken);
            }
        }
    }

    private static string _DeadLetterQueueName(string destination) => destination + "-dlq";
}
