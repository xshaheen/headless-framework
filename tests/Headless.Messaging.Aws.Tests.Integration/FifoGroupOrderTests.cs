// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using Amazon.Runtime;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MessagingHeaders = Headless.Messaging.Headers;

namespace Tests;

/// <summary>
/// Against LocalStack: a FIFO group received in one batch keeps its order when its first message is rejected, even with
/// several handler slots.
/// </summary>
[Collection<LocalStackTestFixture>]
public sealed class FifoGroupOrderTests(LocalStackTestFixture fixture) : TestBase
{
    [Fact]
    public async Task should_handle_a_fifo_group_in_send_order_when_its_first_message_is_rejected_once()
    {
        // given: three messages of one group wait on a FIFO queue before the consumer starts, so one receive takes them
        var destination = $"fifo-order-{Guid.NewGuid():N}.fifo";
        var options = _Options();
        await using var producer = new AmazonSqsQueueTransport(NullLogger<AmazonSqsQueueTransport>.Instance, options);
        await using var consumer = new AmazonSqsConsumerClient(
            destination,
            4,
            options,
            NullLogger<AmazonSqsConsumerClient>.Instance,
            MessageLane.Queue,
            // The test owns its queue, so the client creates it in place of 'resources provision'.
            provisionsTopology: true
        );
        var queueUrls = await consumer.FetchMessageNamesAsync([destination], AbortToken);
        await consumer.SubscribeAsync(queueUrls, AbortToken);

        string[] sent = ["m1", "m2", "m3"];
        foreach (var id in sent)
        {
            var result = await producer.SendAsync(_Message(destination, id), AbortToken);
            result.Succeeded.Should().BeTrue("the send failed with {0}", result.Exception);
        }

        var committed = new ConcurrentQueue<string>();
        var rejectedOnce = 0;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        consumer.AttachCallbacks(
            async (message, sender) =>
            {
                // The core rejects a delivery its inbox could not take in; the first delivery of m1 stands for that.
                if (
                    string.Equals(message.Id, "m1", StringComparison.Ordinal)
                    && Interlocked.Exchange(ref rejectedOnce, 1) == 0
                )
                {
                    await consumer.RejectAsync(sender, CancellationToken.None);
                    return;
                }

                await consumer.CommitAsync(sender, CancellationToken.None);
                committed.Enqueue(message.Id);
                if (committed.Count == sent.Length)
                {
                    done.TrySetResult();
                }
            },
            onLog: null
        );
        using var listening = new CancellationTokenSource();

        // when
        var loop = consumer.ListeningAsync(TimeSpan.FromMilliseconds(100), listening.Token).AsTask();

        try
        {
            await done.Task.WaitAsync(TimeSpan.FromSeconds(60), AbortToken);

            // then: m2 and m3 waited behind the rejected m1 instead of overtaking it
            rejectedOnce.Should().Be(1);
            committed.Should().Equal(sent);
        }
        finally
        {
            await listening.CancelAsync();
            await loop.Awaiting(t => t).Should().ThrowAsync<OperationCanceledException>();
            using var sqs = AwsClientFactory.CreateSqsClient(options.Value);
            await sqs.DeleteQueueAsync(queueUrls.Single(), CancellationToken.None);
        }
    }

    private IOptions<AmazonSqsMessagingOptions> _Options()
    {
        return Options.Create(
            new AmazonSqsMessagingOptions
            {
                Region = Amazon.RegionEndpoint.USEast1,
                SnsServiceUrl = fixture.ConnectionString,
                SqsServiceUrl = fixture.ConnectionString,
                Credentials = new BasicAWSCredentials("test", "test"),
            }
        );
    }

    // Without a routing-affinity key every message of a FIFO destination shares one group.
    private static TransportMessage _Message(string name, string id)
    {
        return new TransportMessage(
            new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [MessagingHeaders.MessageId] = id,
                [MessagingHeaders.MessageName] = name,
            },
            """{"id":1}"""u8.ToArray()
        );
    }
}
