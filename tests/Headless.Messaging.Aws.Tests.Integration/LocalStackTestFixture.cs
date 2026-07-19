// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Headless.Messaging;
using Headless.Messaging.Aws;
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

    public async ValueTask<TransportConsumerConformanceSession> CreateConformanceSessionAsync(
        CancellationToken cancellationToken,
        string? destination = null,
        string? group = null,
        bool ownsQueue = true
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
        var producer = new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, options);
        var consumer = new AmazonSqsConsumerClient(
            group,
            1,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            IntentType.Queue
        );
        var cleanupClient = AwsClientFactory.CreateSqsClient(options.Value);
#pragma warning restore CA2000

        try
        {
            var queueUrls = await consumer.FetchMessageNamesAsync([destination], cancellationToken);
            await consumer.SubscribeAsync(queueUrls, cancellationToken);
            var queueUrl = queueUrls.Single();
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
                createReplacementSession: ownsQueue
                    ? replacementToken =>
                        CreateConformanceSessionAsync(replacementToken, destination, group, ownsQueue: false)
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
