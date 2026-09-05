// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Tests.Capabilities;

namespace Tests;

internal sealed class AwsProviderConformanceDriver(LocalStackTestFixture fixture) : TransportProviderConformanceDriver
{
    private static readonly TransportConformanceProfile _Profile = TransportConformanceManifest.Providers[
        "AWS/LocalStack"
    ];

    public override string ProviderName => _Profile.Provider;

    public override bool SupportsRoutingAffinity => true;

    public override void ConfigureRoutingAffinityTransport(
        Headless.Messaging.Configuration.MessagingSetupBuilder setup
    ) =>
        setup.UseAws(options =>
        {
            options.Region = Amazon.RegionEndpoint.USEast1;
            options.SqsServiceUrl = fixture.ConnectionString;
            options.SnsServiceUrl = fixture.ConnectionString;
            options.Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test");
        });

    public override async Task AssertNativePublisherPathsAsync(CancellationToken cancellationToken)
    {
        await _AssertNativeBusPublisherPathsAsync(cancellationToken);
        var destination = $"native-affinity-{Guid.NewGuid():N}.fifo";
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = fixture.ConnectionString,
            SnsServiceUrl = fixture.ConnectionString,
            Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
        };
        using var client = AwsClientFactory.CreateSqsClient(options);
        string? queueUrl = null;
        try
        {
            await TransportRoutingAffinityConformance.AssertPublisherPathsAsync(
                ConfigureRoutingAffinityTransport,
                destination,
                async (expectedId, token) =>
                {
                    queueUrl ??= (
                        await client.GetQueueUrlAsync(AwsPhysicalAddress.QueueDestination(destination), token)
                    ).QueueUrl;
                    var response = await client.ReceiveMessageAsync(
                        new ReceiveMessageRequest(queueUrl)
                        {
                            WaitTimeSeconds = 10,
                            MaxNumberOfMessages = 1,
                            MessageAttributeNames = ["All"],
                            MessageSystemAttributeNames = ["MessageGroupId"],
                        },
                        token
                    );
                    var native = response.Messages.Should().ContainSingle().Subject;
                    native.Attributes["MessageGroupId"].Should().Be("order-42");
                    native.MessageAttributes.Should().ContainSingle();
                    var headers = JsonSerializer.Deserialize<Dictionary<string, string?>>(
                        native.MessageAttributes["headless-aws-headers-v1"].StringValue
                    )!;
                    headers[Headers.MessageId].Should().Be(expectedId);
                    headers[Headers.RoutingAffinityKey].Should().Be("order-42");
                    headers.Should().ContainKey(Headers.RequestedDeliveryMode);
                    headers.Should().ContainKey(Headers.ResolvedDeliveryMode);
                    await client.DeleteMessageAsync(queueUrl, native.ReceiptHandle, token);
                },
                cancellationToken
            );
        }
        finally
        {
            if (queueUrl is not null)
            {
                await client.DeleteQueueAsync(queueUrl, CancellationToken.None);
            }
        }
    }

    private async Task _AssertNativeBusPublisherPathsAsync(CancellationToken cancellationToken)
    {
        var identity = Guid.NewGuid().ToString("N");
        var destination = $"native-bus-affinity-{identity}.fifo";
        var group = $"native-bus-{identity}.fifo";
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = fixture.ConnectionString,
            SnsServiceUrl = fixture.ConnectionString,
            Credentials = new Amazon.Runtime.BasicAWSCredentials("test", "test"),
        };
        await using var topology = new AmazonSqsConsumerClient(
            group,
            1,
            Options.Create(options),
            NullLogger<AmazonSqsConsumerClient>.Instance,
            MessageLane.Bus
        );
        using var client = AwsClientFactory.CreateSqsClient(options);
        using var sns = AwsClientFactory.CreateSnsClient(options);
        var topics = await topology.FetchMessageNamesAsync([destination], cancellationToken);
        string? queueUrl = null;
        try
        {
            await topology.SubscribeAsync(topics, cancellationToken);
            queueUrl = (
                await client.GetQueueUrlAsync(AwsPhysicalAddress.BusGroupQueue(group), cancellationToken)
            ).QueueUrl;
            await TransportRoutingAffinityConformance.AssertPublisherPathsAsync(
                ConfigureRoutingAffinityTransport,
                destination,
                async (expectedId, token) =>
                {
                    var response = await client.ReceiveMessageAsync(
                        new ReceiveMessageRequest(queueUrl)
                        {
                            WaitTimeSeconds = 10,
                            MaxNumberOfMessages = 1,
                            MessageSystemAttributeNames = ["MessageGroupId"],
                        },
                        token
                    );
                    var native = response.Messages.Should().ContainSingle().Subject;
                    native.Attributes["MessageGroupId"].Should().Be("order-42");
                    using var envelope = JsonDocument.Parse(native.Body);
                    var attributes = envelope.RootElement.GetProperty("MessageAttributes");
                    attributes.GetProperty(Headers.MessageId).GetProperty("Value").GetString().Should().Be(expectedId);
                    attributes
                        .GetProperty(Headers.RoutingAffinityKey)
                        .GetProperty("Value")
                        .GetString()
                        .Should()
                        .Be("order-42");
                    await client.DeleteMessageAsync(queueUrl, native.ReceiptHandle, token);
                },
                cancellationToken,
                lane: MessageLane.Bus
            );
        }
        finally
        {
            if (queueUrl is not null)
            {
                await client.DeleteQueueAsync(queueUrl, CancellationToken.None);
            }
            foreach (var topic in topics)
            {
                await sns.DeleteTopicAsync(topic, CancellationToken.None);
            }
        }
    }

    public override ValueTask<TransportConsumerConformanceSession> CreateRoutingAffinitySessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    ) =>
        CreateSessionAsync(
            endpoint with
            {
                LogicalName = endpoint.LogicalName + ".fifo",
                SubscriberGroup = endpoint.SubscriberGroup + ".fifo",
            },
            cancellationToken
        );

    public override TransportMalformedEnvelopeBound MalformedEnvelopeBound => _Profile.MalformedEnvelopeBound!;

    public override ValueTask<TransportConsumerConformanceSession> CreateSessionAsync(
        TransportConformanceEndpoint endpoint,
        CancellationToken cancellationToken
    )
    {
        var ownsQueue = string.Equals(endpoint.Replica, "replica-1", StringComparison.Ordinal);

        return endpoint.Lane switch
        {
            MessageLane.Bus => fixture.CreateBusSessionAsync(
                endpoint.LogicalName,
                endpoint.SubscriberGroup,
                cancellationToken,
                ownsQueue
            ),
            MessageLane.Queue => fixture.CreateConformanceSessionAsync(
                cancellationToken,
                endpoint.LogicalName,
                endpoint.SubscriberGroup,
                ownsQueue
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(endpoint), endpoint.Lane, null),
        };
    }
}
