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
using SqsMessage = Amazon.SQS.Model.Message;

namespace Tests;

/// <summary>
/// A FIFO group's messages that were never handed over go back to the queue hidden as long as the group's message
/// ahead of them may still be in flight, whichever of the group's own release and shutdown's final release runs first.
/// </summary>
public sealed class AmazonSqsConsumerFifoReleaseTests : TestBase
{
    private const string _QueueUrl = "http://test/000000000000/orders.fifo";

    private readonly TimerSignalingFakeTimeProvider _time = new();
    private readonly IAmazonSQS _sqs = Substitute.For<IAmazonSQS>();
    private readonly ILogger<AmazonSqsConsumerClient> _logger = Substitute.For<ILogger<AmazonSqsConsumerClient>>();
    private readonly List<ChangeMessageVisibilityBatchRequestEntry[]> _visibilityChanges = [];
    private readonly Lock _visibilityLock = new();

    public AmazonSqsConsumerFifoReleaseTests()
    {
        _sqs.ChangeMessageVisibilityAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<int>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(Task.FromResult(new ChangeMessageVisibilityResponse()));
        _OnVisibilityBatch(_ => _Accepted());
    }

    [Fact]
    public async Task should_release_a_waiting_group_when_a_heartbeat_holds_the_visibility_gate_across_shutdown()
    {
        // given: two groups and one handler slot. The first message handed over never finishes, the other group waits
        // for the slot, and a heartbeat extension holds the visibility gate until the test answers it.
        _ReceiveOnce(_Message("A1", "A"), _Message("A2", "A"), _Message("B1", "B"), _Message("B2", "B"));
        var beatInFlight = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishBeat = new TaskCompletionSource<ChangeMessageVisibilityBatchResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var calls = 0;
        _OnVisibilityBatch(_ =>
        {
            if (Interlocked.Increment(ref calls) != 1)
            {
                return _Accepted();
            }

            beatInFlight.TrySetResult();
            return finishBeat.Task;
        });
        var firstStarted = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var giveUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (message, _) =>
        {
            firstStarted.TrySetResult(message.Id!);
            await giveUp.Task;
        };
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        var inFlight = await firstStarted.Task.Bounded();
        _time.Advance(TimeSpan.FromSeconds(10));
        await beatInFlight.Task.Bounded();

        // when: shutdown stops receiving, so the waiting group gives its messages back behind the held gate and gives up
        // after 5 s; then the 7 s budget runs out on the handler that never finishes
        var groupReleaseArmed = _time.WatchForTimer(TimeSpan.FromSeconds(5));
        var drainArmed = _time.WatchForTimer(TimeSpan.FromSeconds(7));
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(7), AbortToken).AsTask();
        await groupReleaseArmed.Bounded();
        await drainArmed.Bounded();
        _time.Advance(TimeSpan.FromSeconds(7));
        finishBeat.TrySetResult(new ChangeMessageVisibilityBatchResponse());
        await shutdown.Bounded();

        // then: after the beat's extension, the waiting group goes back at once, as nothing of it was in flight; the
        // message in flight goes back at once too, and its group's rest stays hidden as long as that message may still
        // be handled
        var (heldGroup, waitingGroup) = inFlight.StartsWith('A', StringComparison.Ordinal) ? ("A", "B") : ("B", "A");
        _ReleasedFor($"{waitingGroup}1").Should().Equal(30, 0);
        _ReleasedFor($"{waitingGroup}2").Should().Equal(30, 0);
        _ReleasedFor($"{heldGroup}1").Should().Equal(30, 0);
        _ReleasedFor($"{heldGroup}2").Should().Equal(30, 30);

        // The receive that holds the stuck message ends only once its handler does.
        giveUp.TrySetResult();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_keep_a_group_s_rest_hidden_while_its_message_outlives_the_shutdown_budget()
    {
        // given: one group, one handler slot, and a handler that never finishes
        _ReceiveOnce(_Message("A1", "A"), _Message("A2", "A"), _Message("A3", "A"));
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var giveUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, _) =>
        {
            handlerStarted.TrySetResult();
            await giveUp.Task;
        };
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        await handlerStarted.Task.Bounded();

        // when: the shutdown budget runs out on the handler
        var drainArmed = _time.WatchForTimer(TimeSpan.FromSeconds(5));
        var shutdown = client.ShutdownAsync(TimeSpan.FromSeconds(5), AbortToken).AsTask();
        await drainArmed.Bounded();
        _time.Advance(TimeSpan.FromSeconds(5));
        await shutdown.Bounded();

        // then: the stuck message goes back at once, and the rest of its group cannot come back ahead of it
        _ReleasedFor("A1").Should().Equal(0);
        _ReleasedFor("A2").Should().Equal(30);
        _ReleasedFor("A3").Should().Equal(30);

        // The receive that holds the stuck message ends only once its handler does.
        giveUp.TrySetResult();
        await _IgnoreCancellationAsync(listening);
    }

    [Fact]
    public async Task should_give_back_a_group_s_rest_with_its_hold_when_the_group_s_own_release_fails_during_shutdown()
    {
        // given: one group whose first message the core rejects with a 20 s delay once shutdown has stopped receiving,
        // and SQS failing the group's own release of the rest
        _ReceiveOnce(_Message("A1", "A"), _Message("A2", "A"), _Message("A3", "A"));
        var attempts = 0;
        _OnVisibilityBatch(entries =>
            entries.TrueForAll(static entry => entry.VisibilityTimeout == 20)
            && Interlocked.Increment(ref attempts) == 1
                ? Task.FromException<ChangeMessageVisibilityBatchResponse>(new AmazonSQSException("unavailable"))
                : _Accepted()
        );
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reject = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 1);
        client.OnMessageCallback = async (_, sender) =>
        {
            handlerStarted.TrySetResult();
            await reject.Task.Bounded();
            await client.RejectAsync(sender, TimeSpan.FromSeconds(20), CancellationToken.None);
        };
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), AbortToken).AsTask();
        await handlerStarted.Task.Bounded();

        // when
        var drainArmed = _time.WatchForTimer(TimeSpan.FromMinutes(1));
        var shutdown = client.ShutdownAsync(TimeSpan.FromMinutes(1), AbortToken).AsTask();
        await drainArmed.Bounded();
        reject.TrySetResult();
        await shutdown.Bounded();
        await listening.Bounded();

        // then: the failed release left the rest tracked, and shutdown gave it back hidden as long as the reject
        attempts.Should().Be(2);
        _ReleasedFor("A2").Should().Equal(20);
        _ReleasedFor("A3").Should().Equal(20);
    }

    [Fact]
    public async Task should_stop_extending_a_group_s_rest_when_its_release_fails_while_the_client_runs()
    {
        // given: groups handled one at a time; A1 is rejected and SQS fails the release of A2, then B1 is held open
        _ReceiveOnce(_Message("A1", "A"), _Message("A2", "A"), _Message("B1", "B"));
        var releaseFailed = 0;
        _OnVisibilityBatch(entries =>
        {
            if (!entries.TrueForAll(static entry => entry.VisibilityTimeout == 3))
            {
                return _Accepted();
            }

            Interlocked.Increment(ref releaseFailed);
            return Task.FromException<ChangeMessageVisibilityBatchResponse>(new AmazonSQSException("unavailable"));
        });
        var b1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishB1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var client = _CreateClient(concurrency: 0);
        client.OnMessageCallback = async (message, sender) =>
        {
            if (string.Equals(message.Id, "A1", StringComparison.Ordinal))
            {
                await client.RejectAsync(sender);
                return;
            }

            // Inline groups run one after another, so group A's release has finished once B1 starts.
            b1Started.TrySetResult();
            await finishB1.Task.Bounded();
            await client.CommitAsync(sender);
        };
        using var cts = new CancellationTokenSource();
        var listening = client.ListeningAsync(TimeSpan.FromSeconds(1), cts.Token).AsTask();
        await b1Started.Task.Bounded();

        // when: a heartbeat runs
        _time.Advance(TimeSpan.FromSeconds(10));
        await _WaitForBeatAsync();

        // then: A2 is no longer kept hidden, so it reappears once its visibility runs out; B1 still is
        releaseFailed.Should().Be(1);
        _ReleasedFor("A2").Should().BeEmpty();
        _ReleasedFor("B1").Should().Equal(30);

        finishB1.TrySetResult();
        await cts.CancelAsync();
        await _IgnoreCancellationAsync(listening);
    }

    private readonly SemaphoreSlim _recorded = new(0);

    private async Task _WaitForBeatAsync()
    {
        await _recorded.WaitAsync(AbortToken).Bounded();
    }

    // Records every visibility change SQS took: the hook answers each call, and a call it fails is not recorded.
    private void _OnVisibilityBatch(
        Func<List<ChangeMessageVisibilityBatchRequestEntry>, Task<ChangeMessageVisibilityBatchResponse>> hook
    )
    {
        _sqs.ChangeMessageVisibilityBatchAsync(
                Arg.Any<string>(),
                Arg.Any<List<ChangeMessageVisibilityBatchRequestEntry>>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(async call =>
            {
                var entries = call.Arg<List<ChangeMessageVisibilityBatchRequestEntry>>();
                var response = await hook(entries);
                lock (_visibilityLock)
                {
                    _visibilityChanges.Add([.. entries]);
                }

                _recorded.Release();
                return response;
            });
    }

    private static Task<ChangeMessageVisibilityBatchResponse> _Accepted()
    {
        return Task.FromResult(new ChangeMessageVisibilityBatchResponse());
    }

    // Every visibility each change SQS took gave the message, in order.
    private int[] _ReleasedFor(string receiptHandle)
    {
        lock (_visibilityLock)
        {
            return
            [
                .. _visibilityChanges
                    .SelectMany(entries => entries)
                    .Where(entry => string.Equals(entry.ReceiptHandle, receiptHandle, StringComparison.Ordinal))
                    .Select(entry => entry.VisibilityTimeout ?? -1),
            ];
        }
    }

    private AmazonSqsConsumerClient _CreateClient(byte concurrency)
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
        _SetField(client, "_queueUrls", ImmutableArray.Create(_QueueUrl));
        return client;
    }

    // The first poll returns the messages; later polls hang like an empty long poll would.
    private void _ReceiveOnce(params SqsMessage[] messages)
    {
        var received = 0;
        _sqs.ReceiveMessageAsync(Arg.Any<ReceiveMessageRequest>(), Arg.Any<CancellationToken>())
            .Returns(call =>
                Interlocked.Increment(ref received) == 1
                    ? Task.FromResult(new ReceiveMessageResponse { Messages = [.. messages] })
                    : _HangUntilCancelledAsync(call.Arg<CancellationToken>())
            );
    }

    private static async Task<ReceiveMessageResponse> _HangUntilCancelledAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(Timeout.Infinite, cancellationToken);
        return new ReceiveMessageResponse();
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

    private static async Task _IgnoreCancellationAsync(Task task)
    {
        try
        {
            await task.Bounded();
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
