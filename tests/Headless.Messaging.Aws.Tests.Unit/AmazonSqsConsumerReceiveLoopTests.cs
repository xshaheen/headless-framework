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
/// Every queue URL has its own receive loop: a long poll on one queue never holds back the messages of another, and a
/// URL added or dropped while listening gains or loses its loop.
/// </summary>
public sealed class AmazonSqsConsumerReceiveLoopTests : TestBase
{
    private const string _BusyUrl = "http://test/busy";
    private const string _EmptyUrl = "http://test/empty";

    private readonly FakeTimeProvider _time = new();
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly ILogger<AmazonSqsConsumerClient> _logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
    private readonly ConcurrentQueue<string> _handled = new();
    private readonly ConcurrentDictionary<string, int> _polls = new(StringComparer.Ordinal);

    public AmazonSqsConsumerReceiveLoopTests()
    {
        _sqs.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DeleteMessageResponse()));
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new ChangeMessageVisibilityBatchResponse()));
    }

    [Fact]
    public async Task should_dispatch_a_busy_queue_without_waiting_for_the_long_poll_of_an_empty_one()
    {
        // given: the empty queue's long poll never returns, and the busy queue has one message
        _ReceiveHangingUntilCancelled(_EmptyUrl);
        _ReceiveOnce(_BusyUrl, "busy-1");
        await using var client = _CreateClient(concurrency: 1, _EmptyUrl, _BusyUrl);
        client.OnMessageCallback = _CommitAndRecord(client);
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then
        await _WaitForHandledAsync(1);
        _handled.Should().Equal("busy-1");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_keep_receiving_from_the_other_queues_while_one_queue_backs_off_after_a_failure()
    {
        // given
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == _EmptyUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns<Task<ReceiveMessageResponse>>(_ => throw new AmazonSQSException("boom"));
        _ReceiveOnce(_BusyUrl, "busy-1");
        await using var client = _CreateClient(concurrency: 1, _EmptyUrl, _BusyUrl);
        client.OnMessageCallback = _CommitAndRecord(client);
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();

        // then: the failing queue's backoff does not discard or delay the busy queue's receive
        await _WaitForHandledAsync(1);
        _handled.Should().Equal("busy-1");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_start_a_loop_for_a_queue_url_added_while_listening()
    {
        // given: listening on one queue
        _ReceiveHangingUntilCancelled(_EmptyUrl);
        _ReceiveOnce(_BusyUrl, "busy-1");
        await using var client = _CreateClient(concurrency: 1, _EmptyUrl);
        client.OnMessageCallback = _CommitAndRecord(client);
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await _WaitForPollAsync(_EmptyUrl);

        // when
        await client.SubscribeAsync([_EmptyUrl, _BusyUrl], AbortToken);

        // then
        await _WaitForHandledAsync(1);
        _handled.Should().Equal("busy-1");

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_stop_polling_a_queue_url_dropped_while_listening()
    {
        // given: both queues are empty and idle between polls
        await using var client = _CreateClient(concurrency: 1, _EmptyUrl, _BusyUrl);
        _ReceiveEmpty(_EmptyUrl);
        _ReceiveEmpty(_BusyUrl);
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await _WaitForPollAsync(_EmptyUrl);
        await _WaitForPollAsync(_BusyUrl);

        // when: the empty queue is dropped, then time passes for several poll rounds
        await client.SubscribeAsync([_BusyUrl], AbortToken);
        var emptyPolls = _polls[_EmptyUrl];
        var busyPolls = _polls[_BusyUrl];
        for (var round = 0; round < 5; round++)
        {
            await _AdvanceAndWaitForPollAsync(_BusyUrl, busyPolls + round + 1);
        }

        // then: at most the poll that was already running when the URL was dropped is made
        _polls[_EmptyUrl].Should().BeLessThanOrEqualTo(emptyPolls + 1);

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_end_every_loop_and_release_unsettled_messages_on_shutdown()
    {
        // given: one queue's message is held by a handler, another queue's long poll is in flight
        _ReceiveHangingUntilCancelled(_EmptyUrl);
        _ReceiveOnce(_BusyUrl, "busy-1");
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1, _EmptyUrl, _BusyUrl);
        client.OnMessageCallback = async (_, sender) =>
        {
            handlerStarted.TrySetResult();
            await finishHandler.Task;
            await client.CommitAsync(sender);
        };

        // The caller never cancels: only shutdown stops listening.
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), CancellationToken.None).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _WaitForPollAsync(_EmptyUrl);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(30), AbortToken).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(50), AbortToken);
        finishHandler.TrySetResult();
        await _AdvanceUntilCompletedAsync(shutdown);

        // then: listening ended normally once the loops stopped, and the handled message was settled by the handler
        await listening.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _sqs.Received(1).DeleteMessageAsync(_BusyUrl, "busy-1", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_surface_the_failure_of_one_loop_and_stop_the_others()
    {
        // given: a handler that throws while handling inline
        _ReceiveHangingUntilCancelled(_EmptyUrl);
        _ReceiveOnce(_BusyUrl, "busy-1");
        await using var client = _CreateClient(concurrency: 0, _EmptyUrl, _BusyUrl);
        client.OnMessageCallback = (_, _) => throw new InvalidOperationException("handler failed");

        // when
        var act = async () =>
            await client
                .ListeningAsync(TimeSpan.FromSeconds(1), CancellationToken.None)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        (await act.Should().ThrowAsync<InvalidOperationException>()).WithMessage("handler failed");
    }

    [Fact]
    public async Task should_give_back_messages_a_long_poll_returns_after_the_consumer_paused()
    {
        // given: a long poll is in flight when the consumer pauses, and returns a message only after the pause
        var inFlightPoll = new TaskCompletionSource<ReceiveMessageResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == _BusyUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                _polls.AddOrUpdate(_BusyUrl, 1, static (_, count) => count + 1);
                return inFlightPoll.Task;
            });
        await using var client = _CreateClient(concurrency: 1, _BusyUrl);
        client.OnMessageCallback = _CommitAndRecord(client);
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await _WaitForPollAsync(_BusyUrl);
        await client.PauseAsync(AbortToken);

        // when
        inFlightPoll.SetResult(new ReceiveMessageResponse { Messages = [_Message("late-1")] });

        // then: the message goes back to the queue at once instead of reaching the handler
        await _WaitForVisibilityResetAsync("late-1");
        _handled.Should().BeEmpty();

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    private Func<TransportMessage, object?, Task> _CommitAndRecord(AmazonSqsConsumerClient client)
    {
        return async (message, sender) =>
        {
            _handled.Enqueue(message.Id);
            await client.CommitAsync(sender);
        };
    }

    private void _ReceiveHangingUntilCancelled(string queueUrl)
    {
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == queueUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _polls.AddOrUpdate(queueUrl, 1, static (_, count) => count + 1);
                return _HangUntilCancelledAsync(call.Arg<CancellationToken>());
            });
    }

    private void _ReceiveEmpty(string queueUrl)
    {
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == queueUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                _polls.AddOrUpdate(queueUrl, 1, static (_, count) => count + 1);
                return Task.FromResult(new ReceiveMessageResponse { Messages = [] });
            });
    }

    // The first poll returns the messages; later polls hang like an empty long poll would.
    private void _ReceiveOnce(string queueUrl, params string[] receiptHandles)
    {
        var received = 0;
        _sqs.ReceiveMessageAsync(
                Arg.Is<ReceiveMessageRequest>(request => request.QueueUrl == queueUrl),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                _polls.AddOrUpdate(queueUrl, 1, static (_, count) => count + 1);

                return Interlocked.Increment(ref received) == 1
                    ? Task.FromResult(new ReceiveMessageResponse { Messages = [.. receiptHandles.Select(_Message)] })
                    : _HangUntilCancelledAsync(call.Arg<CancellationToken>());
            });
    }

    private static async Task<ReceiveMessageResponse> _HangUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new ReceiveMessageResponse();
    }

    private AmazonSqsConsumerClient _CreateClient(byte concurrency, params string[] queueUrls)
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
        _SetField(client, "_queueUrls", ImmutableArray.Create(queueUrls));
        return client;
    }

    private async Task _WaitForHandledAsync(int count)
    {
        for (var i = 0; i < 500 && _handled.Count < count; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
        }

        _handled.Count.Should().BeGreaterThanOrEqualTo(count, "the messages are handled without waiting for a poll");
    }

    private async Task _WaitForVisibilityResetAsync(string receiptHandle)
    {
        bool released() =>
            _sqs.ReceivedCalls()
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
                    && entry.VisibilityTimeout == 0
                );

        for (var i = 0; i < 500 && !released(); i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
        }

        released().Should().BeTrue("a paused consumer hands a late-arriving message back to the queue");
    }

    private async Task _WaitForPollAsync(string queueUrl, int count = 1)
    {
        for (var i = 0; i < 500 && _polls.GetValueOrDefault(queueUrl) < count; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
        }

        _polls.GetValueOrDefault(queueUrl).Should().BeGreaterThanOrEqualTo(count);
    }

    private async Task _AdvanceAndWaitForPollAsync(string queueUrl, int count)
    {
        for (var i = 0; i < 500 && _polls.GetValueOrDefault(queueUrl) < count; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken);
        }

        _polls.GetValueOrDefault(queueUrl).Should().BeGreaterThanOrEqualTo(count);
    }

    // Shutdown registers its drain timer on the fake clock only once it reaches the drain, so time is advanced in steps
    // until it completes.
    private async Task _AdvanceUntilCompletedAsync(Task task)
    {
        for (var step = 0; step < 100 && !task.IsCompleted; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            _ = await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken));
        }

        await task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
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
    }

    // The receipt handle doubles as the message id, so a callback can tell the messages apart.
    private static SqsMessage _Message(string receiptHandle)
    {
        return new SqsMessage
        {
            Body = "{}",
            ReceiptHandle = receiptHandle,
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
