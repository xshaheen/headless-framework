// Copyright (c) Mahmoud Shaheen. All rights reserved.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using Amazon.SQS;
using Amazon.SQS.Model;
using Headless.Messaging;
using Headless.Messaging.Aws;
using Headless.Testing.Tests;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using SqsMessage = Amazon.SQS.Model.Message;

namespace Tests;

/// <summary>
/// On a FIFO queue the messages of one group in a receive are handed over one at a time in receive order, and a message
/// that is not committed holds the rest of its group behind it; other groups and standard queues are not held.
/// </summary>
public sealed class AmazonSqsConsumerFifoOrderTests : TestBase
{
    private const string _FifoUrl = "http://test/000000000000/orders.fifo";
    private const string _StandardUrl = "http://test/000000000000/orders";

    private readonly FakeTimeProvider _time = new();
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly ILogger<AmazonSqsConsumerClient> _logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
    private readonly ConcurrentQueue<string> _started = new();
    private readonly ConcurrentQueue<string> _committed = new();
    private readonly ConcurrentQueue<ReceiveMessageRequest> _requests = new();

    public AmazonSqsConsumerFifoOrderTests()
    {
        _sqs.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeleteMessageResponse()));
        _sqs.DeleteMessageBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<DeleteMessageBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
                Task.FromResult(
                    new DeleteMessageBatchResponse
                    {
                        Successful =
                        [
                            .. call.Arg<List<DeleteMessageBatchRequestEntry>>()
                                .Select(entry => new DeleteMessageBatchResultEntry { Id = entry.Id }),
                        ],
                    }
                )
            );
        _sqs.ChangeMessageVisibilityAsync(Arg.Any<ChangeMessageVisibilityRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ChangeMessageVisibilityResponse()));
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new ChangeMessageVisibilityBatchResponse()));
    }

    [Fact]
    public async Task should_handle_one_group_in_receive_order_while_another_group_runs_in_parallel()
    {
        // given: two handler slots, and a receive of A1, A2 (group A) then B1 (group B)
        _ReceiveOnce(_FifoUrl, ("A1", "A"), ("A2", "A"), ("B1", "B"));
        var b1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 2, _FifoUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            _started.Enqueue(message.Id);
            if (string.Equals(message.Id, "B1", StringComparison.Ordinal))
            {
                b1Started.TrySetResult();
            }
            else if (string.Equals(message.Id, "A1", StringComparison.Ordinal))
            {
                // A1 finishes only once B1 runs beside it: a second slot spent on A2 would never let B1 start.
                await b1Started.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
            }

            await client.CommitAsync(sender);
            _committed.Enqueue(message.Id);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        await _WaitUntilAsync(() => _committed.Count == 3, "every message of the receive is handled");
        _committed.Where(id => id.StartsWith('A')).Should().Equal("A1", "A2");
        _started.Where(id => id.StartsWith('A')).Should().Equal("A1", "A2");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public async Task should_return_the_rest_of_a_group_to_the_queue_when_its_message_is_rejected(int concurrency)
    {
        // given: A1 is rejected, as the core does when it cannot take a delivery in
        _ReceiveOnce(_FifoUrl, ("A1", "A"), ("A2", "A"), ("B1", "B"), ("A3", "A"));
        await using var client = _CreateClient((byte)concurrency, _FifoUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            _started.Enqueue(message.Id);
            if (string.Equals(message.Id, "A1", StringComparison.Ordinal))
            {
                await client.RejectAsync(sender);
                return;
            }

            await client.CommitAsync(sender);
            _committed.Enqueue(message.Id);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then: the other group is handled, and A2 and A3 go back to the queue hidden as long as A1's reject delay, so
        // they cannot come back ahead of it, without reaching the handler
        await _WaitUntilAsync(
            () => _committed.Contains("B1") && _HiddenFor("A2", 3) && _HiddenFor("A3", 3),
            "the rest of the rejected message's group is returned to the queue"
        );
        _started.Should().NotContain(["A2", "A3"]);

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_hide_the_rest_of_a_group_as_long_as_a_delayed_reject()
    {
        // given: the core rejects A1 with a redelivery delay, as it does after a failed inbox write
        _ReceiveOnce(_FifoUrl, ("A1", "A"), ("A2", "A"));
        await using var client = _CreateClient(concurrency: 2, _FifoUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            _started.Enqueue(message.Id);
            await client.RejectAsync(sender, TimeSpan.FromSeconds(20), CancellationToken.None);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        await _WaitUntilAsync(() => _HiddenFor("A2", 20), "the rest of the group waits out A1's redelivery delay");
        _started.Should().Equal("A1");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_return_the_rest_of_a_group_when_its_handler_throws()
    {
        // given
        _ReceiveOnce(_FifoUrl, ("A1", "A"), ("A2", "A"));
        await using var client = _CreateClient(concurrency: 2, _FifoUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            _started.Enqueue(message.Id);
            if (string.Equals(message.Id, "A1", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("handler failed");
            }

            await client.CommitAsync(sender);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        // A1 reappears only after its visibility timeout (30 s by default), so A2 stays hidden as long.
        await _WaitUntilAsync(() => _HiddenFor("A2", 30), "the unsettled message's group is returned behind it");
        _started.Should().Equal("A1");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_keep_handling_a_standard_queue_after_a_rejected_message()
    {
        // given: a standard queue promises no order, so a rejection holds nothing back
        _ReceiveOnce(_StandardUrl, ("A1", "A"), ("A2", "A"));
        await using var client = _CreateClient(concurrency: 1, _StandardUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            _started.Enqueue(message.Id);
            if (string.Equals(message.Id, "A1", StringComparison.Ordinal))
            {
                await client.RejectAsync(sender);
                return;
            }

            await client.CommitAsync(sender);
            _committed.Enqueue(message.Id);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        await _WaitUntilAsync(() => _committed.Contains("A2"), "the next message is handled");
        _started.Should().Equal("A1", "A2");
        _requests.Should().AllSatisfy(r => r.MessageSystemAttributeNames.Should().NotContain("MessageGroupId"));

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_ask_a_fifo_queue_for_the_message_group_id()
    {
        // given
        _ReceiveOnce(_FifoUrl, ("A1", "A"));
        await using var client = _CreateClient(concurrency: 1, _FifoUrl);
        client.OnMessageCallback = async (message, sender) =>
        {
            await client.CommitAsync(sender);
            _committed.Enqueue(message.Id);
        };
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        await _WaitUntilAsync(() => _committed.Count == 1, "the message is handled");
        _requests
            .Should()
            .AllSatisfy(r => r.MessageSystemAttributeNames.Should().Contain(["MessageGroupId", "SentTimestamp"]));

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    private bool _HiddenFor(string receiptHandle, int seconds)
    {
        return _sqs.ReceivedCalls()
            .Where(call =>
                string.Equals(
                    call.GetMethodInfo().Name,
                    nameof(IAmazonSQS.ChangeMessageVisibilityBatchAsync),
                    StringComparison.Ordinal
                )
            )
            .SelectMany(call => (List<ChangeMessageVisibilityBatchRequestEntry>)call.GetArguments()[1]!)
            .Any(entry =>
                string.Equals(entry.ReceiptHandle, receiptHandle, StringComparison.Ordinal)
                && entry.VisibilityTimeout == seconds
            );
    }

    // The first poll returns the messages; later polls hang like an empty long poll would.
    private void _ReceiveOnce(string queueUrl, params (string ReceiptHandle, string GroupId)[] messages)
    {
        var received = 0;
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == queueUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _requests.Enqueue(call.Arg<ReceiveMessageRequest>());

                return Interlocked.Increment(ref received) == 1
                    ? Task.FromResult(
                        new ReceiveMessageResponse
                        {
                            Messages = [.. messages.Select(m => _Message(m.ReceiptHandle, m.GroupId))],
                        }
                    )
                    : _HangUntilCancelledAsync(call.Arg<CancellationToken>());
            });
    }

    private static async Task<ReceiveMessageResponse> _HangUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new ReceiveMessageResponse();
    }

    private AmazonSqsConsumerClient _CreateClient(byte concurrency, string queueUrl)
    {
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
            SnsServiceUrl = "http://localhost:4566",
        };

        var client = new AmazonSqsConsumerClient(
            "orders",
            concurrency,
            Options.Create(options),
            _logger,
            MessageLane.Queue,
            _time
        );
        _SetField(client, "_sqsClient", _sqs);
        _SetField(client, "_queueUrls", ImmutableArray.Create(queueUrl));
        return client;
    }

    private static async Task _WaitUntilAsync(Func<bool> condition, string because)
    {
        for (var i = 0; i < 500 && !condition(); i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
        }

        condition().Should().BeTrue(because);
    }

    private static async Task _IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        }
        catch (OperationCanceledException)
        {
            // Expected: the caller cancelled listening.
        }
        catch (InvalidOperationException)
        {
            // Expected when an inline handler throws: the loop surfaces its failure.
        }
    }

    // The receipt handle doubles as the message id, so a callback can tell the messages apart.
    private static SqsMessage _Message(string receiptHandle, string groupId)
    {
        return new SqsMessage
        {
            Body = "{}",
            ReceiptHandle = receiptHandle,
            Attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["MessageGroupId"] = groupId },
            MessageAttributes = new Dictionary<string, MessageAttributeValue>(StringComparer.Ordinal)
            {
                [SqsHeaderCodec.AttributeName] = new()
                {
                    DataType = "String",
                    StringValue = JsonSerializer.Serialize(
                        new Dictionary<string, string>(StringComparer.Ordinal)
                        {
                            [Headers.MessageId] = receiptHandle,
                            [Headers.MessageName] = "orders",
                        }
                    ),
                },
            },
        };
    }

    private static void _SetField(AmazonSqsConsumerClient client, string name, object value)
    {
        typeof(AmazonSqsConsumerClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(client, value);
    }
}
