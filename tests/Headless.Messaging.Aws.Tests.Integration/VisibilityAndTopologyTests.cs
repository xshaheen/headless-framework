// Copyright (c) Mahmoud Shaheen. All rights reserved.

using Amazon.Runtime;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Against LocalStack: a handler slower than the visibility timeout is not redelivered, and with AutoProvision off a Bus
/// consumer and publisher run on externally created topology through raw message delivery.
/// </summary>
[Collection<LocalStackTestFixture>]
public sealed class VisibilityAndTopologyTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_not_redeliver_a_message_whose_handler_outlives_the_visibility_timeout()
    {
        // given: a 2 s visibility timeout and a handler that takes three times as long
        var options = _Options(o => o.VisibilityTimeout = TimeSpan.FromSeconds(2));
        var destination = $"slow-{Guid.NewGuid():N}";
        await using var producer = new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, options);
        await using var consumer = new AmazonSqsConsumerClient(
            destination,
            1,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            MessageLane.Queue
        );
        var queueUrls = await consumer.FetchMessageNamesAsync([destination], AbortToken);
        await consumer.SubscribeAsync(queueUrls, AbortToken);

        var deliveries = 0;
        var settled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.AttachCallbacks(
            async (_, sender) =>
            {
                Interlocked.Increment(ref deliveries);
                await Task.Delay(TimeSpan.FromSeconds(6), AbortToken);
                await consumer.CommitAsync(sender, CancellationToken.None);
                settled.TrySetResult();
            },
            onLog: null
        );
        using var listening = new CancellationTokenSource();
        var loop = consumer.ListeningAsync(TimeSpan.FromMilliseconds(100), listening.Token).AsTask();

        // when
        (await producer.SendAsync(_Message(destination), AbortToken))
            .Succeeded.Should()
            .BeTrue();
        await settled.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

        // then: one delivery, and the delete with the original receipt removed the message
        deliveries.Should().Be(1);
        using var sqs = AwsClientFactory.CreateSqsClient(options.Value);
        (await _PendingAsync(sqs, queueUrls.Single())).Should().Be(0);

        await listening.CancelAsync();
        await loop.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
        await sqs.DeleteQueueAsync(queueUrls.Single(), CancellationToken.None);
    }

    [Fact]
    public async Task should_consume_bus_messages_on_externally_created_topology_without_provisioning()
    {
        // given: the topic, the queue, its policy, and a raw-delivery subscription are created outside the transport
        var identity = $"external-{Guid.NewGuid():N}";
        var messageName = $"ExternalEvent{Guid.NewGuid():N}";
        var options = _Options(o => o.AutoProvision = false);
        using var sqs = AwsClientFactory.CreateSqsClient(options.Value);
        using var sns = AwsClientFactory.CreateSnsClient(options.Value);
        var topicArn = (await sns.CreateTopicAsync(AwsPhysicalAddress.BusTopic(messageName), AbortToken)).TopicArn;
        var queueUrl = (
            await sqs.CreateQueueAsync(AwsPhysicalAddress.BusSubscriptionQueue(identity), AbortToken)
        ).QueueUrl;
        var queueArn = (await sqs.GetAttributesAsync(queueUrl))["QueueArn"];
        await sns.SubscribeAsync(
            new SubscribeRequest
            {
                TopicArn = topicArn,
                Protocol = "sqs",
                Endpoint = queueArn,
                Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["RawMessageDelivery"] = "true" },
            },
            AbortToken
        );

        await using var producer = new AmazonSnsBusTransport(NullLogger<AmazonSnsBusTransport>.Instance, options);
        await using var consumer = new AmazonSqsConsumerClient(
            identity,
            1,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            MessageLane.Bus
        );
        var names = await consumer.FetchMessageNamesAsync([messageName], AbortToken);
        await consumer.SubscribeAsync(names, AbortToken);

        var received = new TaskCompletionSource<TransportMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.AttachCallbacks(
            async (message, sender) =>
            {
                await consumer.CommitAsync(sender, CancellationToken.None);
                received.TrySetResult(message);
            },
            onLog: null
        );
        using var listening = new CancellationTokenSource();
        var loop = consumer.ListeningAsync(TimeSpan.FromMilliseconds(100), listening.Token).AsTask();

        try
        {
            // when
            var result = await producer.SendAsync(_Message(messageName, ("tenant", "acme")), AbortToken);
            result.Succeeded.Should().BeTrue("the send failed with {0}", result.Exception);
            var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(30), AbortToken);

            // then: the consumer reads the raw body and the header bag, and created nothing of its own
            message.Headers[MessagingHeaders.MessageName].Should().Be(messageName);
            message.Headers["tenant"].Should().Be("acme");
            Encoding.UTF8.GetString(message.Body.Span).Should().Be("""{"id":1}""");
            (await sns.ListSubscriptionsByTopicAsync(topicArn, AbortToken)).Subscriptions.Should().ContainSingle();
        }
        finally
        {
            await listening.CancelAsync();
            await loop.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
            await sqs.DeleteQueueAsync(queueUrl, CancellationToken.None);
            await sns.DeleteTopicAsync(topicArn, CancellationToken.None);
        }
    }

    [Fact]
    public async Task should_fail_to_start_a_consumer_whose_queue_does_not_exist_without_provisioning()
    {
        // given
        await using var consumer = new AmazonSqsConsumerClient(
            $"missing-{Guid.NewGuid():N}",
            1,
            _Options(o => o.AutoProvision = false),
            NullLogger<AmazonSqsConsumerClient>.Instance,
            MessageLane.Queue
        );

        // when
        var act = async () => await consumer.FetchMessageNamesAsync([$"missing-{Guid.NewGuid():N}"], AbortToken);

        // then
        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*sqs:GetQueueUrl*AutoProvision is off*");
    }

    private IOptions<AmazonSqsMessagingOptions> _Options(Action<AmazonSqsMessagingOptions> configure)
    {
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SnsServiceUrl = fixture.ConnectionString,
            SqsServiceUrl = fixture.ConnectionString,
            Credentials = new BasicAWSCredentials("test", "test"),
        };
        configure(options);
        return Options.Create(options);
    }

    private static TransportMessage _Message(string name, params (string Key, string Value)[] headers)
    {
        var all = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [MessagingHeaders.MessageId] = Guid.NewGuid().ToString("N"),
            [MessagingHeaders.MessageName] = name,
        };

        foreach (var (key, value) in headers)
        {
            all[key] = value;
        }

        return new TransportMessage(all, """{"id":1}"""u8.ToArray());
    }

    private static async Task<int> _PendingAsync(IAmazonSQS sqs, string queueUrl)
    {
        var response = await sqs.GetQueueAttributesAsync(
            queueUrl,
            [QueueAttributeName.ApproximateNumberOfMessages, QueueAttributeName.ApproximateNumberOfMessagesNotVisible]
        );

        return response.ApproximateNumberOfMessages + response.ApproximateNumberOfMessagesNotVisible;
    }
}
