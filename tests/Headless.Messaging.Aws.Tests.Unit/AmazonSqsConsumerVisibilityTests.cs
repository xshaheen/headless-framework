// Copyright (c) Mahmoud Shaheen. All rights reserved.

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
/// The consumer keeps every unsettled message hidden while it waits for a handler or is handled, stops once the core
/// settles it, drains handlers on shutdown, and gives back what it never handed over.
/// </summary>
public sealed class AmazonSqsConsumerVisibilityTests : TestBase
{
    private const string _QueueUrl = "http://test/queue";

    private readonly FakeTimeProvider _time = new();
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly ILogger<AmazonSqsConsumerClient> _logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
    private readonly List<ChangeMessageVisibilityBatchRequestEntry[]> _visibilityChanges = [];
    private readonly Lock _visibilityLock = new();
    private readonly SemaphoreSlim _visibilityChanged = new(0);
    private readonly SemaphoreSlim _deleted = new(0);

    public AmazonSqsConsumerVisibilityTests()
    {
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                lock (_visibilityLock)
                {
                    _visibilityChanges.Add([.. call.Arg<List<ChangeMessageVisibilityBatchRequestEntry>>()]);
                }

                _visibilityChanged.Release();
                return Task.FromResult(new ChangeMessageVisibilityBatchResponse());
            });
        _sqs.DeleteMessageAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                _deleted.Release();
                return Task.FromResult(new DeleteMessageResponse());
            });
    }

    [Fact]
    public async Task should_receive_with_the_configured_visibility_and_wait_time()
    {
        // given
        var polled = new TaskCompletionSource<ReceiveMessageRequest>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                polled.TrySetResult(call.Arg<ReceiveMessageRequest>());
                return new ReceiveMessageResponse { Messages = [] };
            });
        await using var client = _CreateClient(
            concurrency: 1,
            options =>
            {
                options.VisibilityTimeout = TimeSpan.FromMinutes(2);
                options.ReceiveWaitTime = TimeSpan.FromSeconds(20);
            }
        );
        using var cts = new CancellationTokenSource();

        // when
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        var request = await polled.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        request.VisibilityTimeout.Should().Be(120);
        request.WaitTimeSeconds.Should().Be(20);
        request.MaxNumberOfMessages.Should().Be(10);

        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_extend_visibility_of_handled_and_waiting_messages_until_they_are_settled()
    {
        // given: two messages in one receive, a single handler slot, and the first handler held open
        _ReceiveOnce(_Message("receipt-1"), _Message("receipt-2"));
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (message, sender) =>
        {
            if (message.Id == "receipt-1")
            {
                handlerStarted.TrySetResult();
                await finishHandler.Task;
            }

            await client.CommitAsync(sender);
        };
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when: a third of the 30 s visibility passes
        _time.Advance(TimeSpan.FromSeconds(10));
        await _visibilityChanged.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then: the handled message and the one waiting for the slot are both extended by the full timeout
        var beat = _VisibilityChanges().Single();
        beat.Select(entry => entry.ReceiptHandle).Should().BeEquivalentTo("receipt-1", "receipt-2");
        beat.Should().OnlyContain(entry => entry.VisibilityTimeout == 30);

        // when: both are settled
        finishHandler.TrySetResult();
        await _deleted.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await _deleted.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        _time.Advance(TimeSpan.FromSeconds(10));

        // then: the next beat has nothing left to extend
        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
        _VisibilityChanges().Should().ContainSingle();
    }

    [Fact]
    public async Task should_stop_extending_a_rejected_message()
    {
        // given
        _ReceiveOnce(_Message("receipt-1"));
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, sender) =>
        {
            await client.RejectAsync(sender);
            rejected.TrySetResult();
        };
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await rejected.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        _time.Advance(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);

        // then: the short reject visibility is not overwritten by a heartbeat
        _VisibilityChanges().Should().BeEmpty();
        await _sqs.Received(1).ChangeMessageVisibilityAsync(_QueueUrl, "receipt-1", 3, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task should_send_a_reject_only_after_a_concurrent_extension_of_the_same_message_returned()
    {
        // given: a heartbeat call for the message is in flight when the core rejects it
        _ReceiveOnce(_Message("receipt-1"));
        var beatInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishBeat = new TaskCompletionSource<ChangeMessageVisibilityBatchResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(_ =>
            {
                beatInFlight.TrySetResult();
                return finishBeat.Task;
            });
        var rejectSentAfterBeat = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _sqs.ChangeMessageVisibilityAsync(_QueueUrl, "receipt-1", 3, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                rejectSentAfterBeat.TrySetResult(finishBeat.Task.IsCompleted);
                return Task.FromResult(new ChangeMessageVisibilityResponse());
            });
        var reject = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, sender) =>
        {
            await reject.Task;
            await client.RejectAsync(sender);
        };
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await _WhenReceivedAsync();
        _time.Advance(TimeSpan.FromSeconds(10));
        await beatInFlight.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        reject.TrySetResult();
        finishBeat.TrySetResult(new ChangeMessageVisibilityBatchResponse());

        // then: the short reject visibility is the last word
        (await rejectSentAfterBeat.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken))
            .Should()
            .BeTrue();
        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_keep_extending_after_a_service_fault_and_drop_a_message_sqs_refuses()
    {
        // given: two held messages; SQS refuses the first for a reason of the request and fails the second on its side
        _ReceiveOnce(_Message("receipt-refused"), _Message("receipt-retried"));
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(call =>
            {
                var entries = call.Arg<List<ChangeMessageVisibilityBatchRequestEntry>>();
                lock (_visibilityLock)
                {
                    _visibilityChanges.Add([.. entries]);
                }

                _visibilityChanged.Release();
                return Task.FromResult(
                    new ChangeMessageVisibilityBatchResponse
                    {
                        Failed =
                        [
                            .. entries.Select(entry => new BatchResultErrorEntry
                            {
                                Id = entry.Id,
                                Code =
                                    entry.ReceiptHandle == "receipt-refused"
                                        ? "ReceiptHandleIsInvalid"
                                        : "InternalError",
                                SenderFault = entry.ReceiptHandle == "receipt-refused",
                            }),
                        ],
                    }
                );
            });
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = 0;
        var bothStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 2);
        client.OnMessageCallback = async (_, _) =>
        {
            if (Interlocked.Increment(ref started) == 2)
            {
                bothStarted.TrySetResult();
            }

            await held.Task;
        };
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await bothStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when: two beats
        _time.Advance(TimeSpan.FromSeconds(10));
        await _visibilityChanged.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        _time.Advance(TimeSpan.FromSeconds(10));
        await _visibilityChanged.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // then
        var beats = _VisibilityChanges();
        beats[0].Select(entry => entry.ReceiptHandle).Should().BeEquivalentTo("receipt-refused", "receipt-retried");
        beats[1].Select(entry => entry.ReceiptHandle).Should().Equal("receipt-retried");

        held.TrySetResult();
        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_drain_a_running_handler_before_disposing_the_sqs_client()
    {
        // given
        _ReceiveOnce(_Message("receipt-1"));
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, sender) =>
        {
            handlerStarted.TrySetResult();
            await finishHandler.Task;
            await client.CommitAsync(sender);
        };
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromMinutes(1), AbortToken).AsTask();

        // then: shutdown waits for the handler
        shutdown.IsCompleted.Should().BeFalse();
        _sqs.DidNotReceive().Dispose();

        finishHandler.TrySetResult();
        await shutdown.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
        await listening.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        Received.InOrder(() =>
        {
            _ = _sqs.DeleteMessageAsync(_QueueUrl, "receipt-1", Arg.Any<CancellationToken>());
            _sqs.Dispose();
        });
    }

    [Fact]
    public async Task should_release_what_is_left_when_a_handler_outlives_the_shutdown_budget()
    {
        // given: three messages, one slot, and a handler that never finishes
        _ReceiveOnce(_Message("receipt-1"), _Message("receipt-2"), _Message("receipt-3"));
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, _) =>
        {
            handlerStarted.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, AbortToken);
        };
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

        // when
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(5), AbortToken).AsTask();
        await _AdvanceUntilCompletedAsync(shutdown);

        // then: the two never handed over and the stuck one are made visible at once, then the client is disposed
        var released = _VisibilityChanges()
            .SelectMany(entries => entries)
            .Where(entry => entry.VisibilityTimeout == 0)
            .Select(entry => entry.ReceiptHandle);
        released.Should().BeEquivalentTo("receipt-1", "receipt-2", "receipt-3");
        _sqs.Received(1).Dispose();
        await listening.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);
    }

    private AmazonSqsConsumerClient _CreateClient(byte concurrency, Action<AmazonSqsMessagingOptions>? configure = null)
    {
        var options = new AmazonSqsMessagingOptions
        {
            Region = Amazon.RegionEndpoint.USEast1,
            SqsServiceUrl = "http://localhost:4566",
            SnsServiceUrl = "http://localhost:4566",
        };
        configure?.Invoke(options);

        var client = new AmazonSqsConsumerClient(
            "orders",
            concurrency,
            Options.Create(options),
            _logger,
            MessageLane.Queue,
            _time
        );
        _SetField(client, "_sqsClient", _sqs);
        _SetField(client, "_queueUrls", ImmutableArray.Create(_QueueUrl));
        return client;
    }

    private readonly TaskCompletionSource _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Task _WhenReceivedAsync() => _received.Task.WaitAsync(TimeSpan.FromSeconds(10), AbortToken);

    private void _ReceiveOnce(params SqsMessage[] messages)
    {
        var received = 0;
        _sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (Interlocked.Increment(ref received) == 1)
                {
                    return new ReceiveMessageResponse { Messages = [.. messages] };
                }

                // A second poll means the first receive was handed out.
                _received.TrySetResult();
                return new ReceiveMessageResponse { Messages = [] };
            });
    }

    // The message id doubles as the receipt handle, so a callback can tell the messages apart.
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

    private ChangeMessageVisibilityBatchRequestEntry[][] _VisibilityChanges()
    {
        lock (_visibilityLock)
        {
            return [.. _visibilityChanges];
        }
    }

    // Shutdown registers its drain timer on the fake clock only once it reaches the drain, so time is advanced in steps
    // until it completes.
    private async Task _AdvanceUntilCompletedAsync(Task task)
    {
        for (var step = 0; step < 100 && !task.IsCompleted; step++)
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(20), AbortToken));
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

    private static void _SetField(AmazonSqsConsumerClient client, string name, object value)
    {
        typeof(AmazonSqsConsumerClient)
            .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)!
            .SetValue(client, value);
    }
}
